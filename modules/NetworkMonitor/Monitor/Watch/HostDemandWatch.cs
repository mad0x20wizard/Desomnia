using Autofac;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Watch
{
    public abstract class HostDemandWatch : NetworkHostWatch
    {
        private int _requestNr = 1;

        public abstract bool IsOnline { get; }

        protected bool HasOngoingRequests => !_ongoingRequests.IsEmpty;

        protected virtual int MaxConcurrentRequests => DemandOptions.Parallel;

        public required ILifetimeScope Scope { protected get; init; }

        public required NetworkDevice   Device  { protected get; init; }
        public required NetworkSegment  Network { protected internal get; init; }

        public required AdvertiseOptions        AdvertiseOptions    { get; init; }
        public required HandoffOptions          HandoffOptions      { get; init; }
        public required DemandOptions           DemandOptions       { get; init; }

        public required Lazy<IPacketFilter>     Filter              { internal get; init; }

        /// <summary>The configured baseline, as decided when the watch was constructed.</summary>
        public required PacketFilterOptions     DefaultFilterOptions { internal get; init; }

        /// <summary>
        /// The effective filter options, determined dynamically: filter rules and service watches
        /// can appear and disappear at runtime, so they cannot be decided statically.
        /// Queried by the BPF engine to detect placeholder hosts still awaiting their services.
        /// </summary>
        public PacketFilterOptions FilterOptions
        {
            get
            {
                var options = DefaultFilterOptions;

                bool services = this.OfType<ServiceFilterWatch>().Any();

                options.BlockByDefault |= services || Filter.Value.Rules.Any(rule => rule.Type == FilterRuleType.Must);
                options.NeedsIPTraffic |= services || Filter.Value.Rules.Any(rule => rule is IPFilterRule);

                options.AwaitServices &= !services; // no longer a placeholder once services registered

                return options;
            }
        }

        readonly ConcurrentDictionary<object, DemandRequest> _ongoingRequests = [];

        public object DetermineSource(IPAddress source)
        {
            switch (DemandOptions.Source)
            {
                case DemandSource.Host when Network[source] is NetworkHost host:
                    return host;

                default:
                    return source;
            }
        }

        internal virtual DemandRequest? Evaluate(INetworkCapture capture)
        {
            /**
             * Magic Packets should never start a demand request,
             * but shall be allowed to trigger special actions,
             * like waking up a virtual host immediately
             * or responding with an address advertisement in order
             * to signal the presence of the target host.
             */
            if (capture.IsMagicPacket())
            {
                HandleMagicPacket(capture);
            }

            /**
             * Only packets with a valid source IP address will be processed
             * and can trigger a demand request.
             */
            else if (capture.SourceAddress is IPAddress source)
            {
                // do we already have an ongoing request for that source?
                if (_ongoingRequests.TryGetValue(DetermineSource(source), out DemandRequest? ongoing))
                {
                    ongoing.EnqueuePacket(capture);
                }

                // An established TCP stream can no longer start a demand request. Account for it
                // through the same filters, without starting a request.
                else if (capture.FindPacket<TcpPacket>() is { Synchronize: false })
                {
                    ReportNetworkTraffic(capture, FlowDirection.Inbound);
                }

                // can we start a new request for that source Options?
                else if (MaybeStartRequest(capture) is DemandRequest request)
                {
                    request.EnqueuePacket(capture);

                    return request;
                }
            }

            return null;
        }

        protected virtual void HandleMagicPacket(INetworkCapture packet)
        {
            _ = Event(nameof(MagicPacket)).TriggerEventAsync(new DemandEvent(Host, packets: [packet])
            {
                // TODO: is something missing?
            });
        }

        private bool CanTriggerDemand(INetworkCapture capture)
        {
            if (capture.FindPacket<TcpPacket>() is TcpPacket tcp)
            {
                if (tcp.Synchronize) // ongoing TCP connections never trigger demand requests
                {
                    bool hasServiceDemand = this.Any(service => service.CanTriggerDemand(capture));

                    return !IsOnline || hasServiceDemand;
                }
            }
            else if (!IsOnline)
            {
                return true;
            }

            return false;
        }

        private protected virtual bool ShouldStartRequest(INetworkCapture capture)
        {
            try
            {
                if (Verify(capture))
                {
                    if (CanTriggerDemand(capture))
                        return true;

                    ReportNetworkTraffic(capture, FlowDirection.Inbound);

                    return false;
                }

                return false; // shortcut successfully taken
            }
            catch (AdditionalDataNeededException)
            {
                return true; // to gather more information, we now need to start a request
            }
        }

        private DemandRequest? MaybeStartRequest(INetworkCapture trigger)
        {
            if (ShouldStartRequest(trigger))
            {
                if (_ongoingRequests.Count >= MaxConcurrentRequests)
                    return null;

                ILifetimeScope scope = Scope.BeginLifetimeScope(MatchingScopeLifetimeTags.RequestLifetimeScopeTag);

                var request = scope.Resolve<DemandRequest>(TypedParameter.From(trigger));
                request.Number = _requestNr++;

                scope.CurrentScopeEnding += (sender, args) => EndRequest(request);

                _ongoingRequests[DetermineSource(request.SourceAddress)] = request;

                return request;
            }

            return null;
        }

        public bool Verify(INetworkCapture packet, FlowDirection direction = FlowDirection.Inbound)
        {
            /*
             * Packets accepted by a service watch are judged by that service's own filter, starting
             * from the configured baseline. Everything else runs against the effective options,
             * which already account for present service watches and Must-rules (see FilterOptions).
             */
            foreach (var serviceWatch in this.OfType<ServiceFilterWatch>().Where(w => w.Service.Accepts(packet, direction)))
            {
                return !serviceWatch.Filter.Value.ShouldFilter(packet, DefaultFilterOptions, direction);
            }

            return !Filter.Value.ShouldFilter(packet, FilterOptions, direction);
        }

        protected internal override void ReportNetworkTraffic(INetworkCapture packet, FlowDirection direction)
        {
            try
            {
                if (Verify(packet, direction))
                {
                    base.ReportNetworkTraffic(packet, direction);
                }
            }
            catch (AdditionalDataNeededException)
            {
                // Accounting never starts a request to gather missing information.
            }
            catch (MalformedServicePayloadException)
            {
                // Malformed payloads cannot contribute accepted traffic either.
            }
        }

        internal protected virtual async Task<PhysicalAddress?> RequestIPUnicastTrafficTo(IPAddress ip)
        {
            return null;
        }

        internal async Task ReportDemand(DemandRequest request, bool forward = false)
        {
            var @event = new DemandEvent(request)
            {
                CanBeForwarded = forward
            };

            var watch = this.FirstOrDefault(watch => request.Any(packet => watch.Service.Accepts(packet)));

            @event.Service = watch?.Service;

            ReportNetworkTraffic(@event);

            await TriggerDemandAsync(@event);

            if (watch != null)
            {
                await watch.TriggerDemandAsync(@event);
            }
        }

        protected void ForwardPackets(IEnumerable<INetworkCapture> packets, bool onlyIPUnicast = true)
        {
            if (onlyIPUnicast)
            {
                packets = packets.Where(packet => packet.IsIPUnicast);
            }

            Logger.LogTrace($"FORWARD {packets.Count()} packet(s) to '{Host.Name}':");

            foreach (EthernetPacket packet in packets.SelectMany(capture => capture))
            {
                var copy = packet.MakeCapturedCopy();

                /**
                 * Some VPN network interfaces (like OpenVPN) don't reveal their own MAC address.
                 * In any other case, we should always use our own MAC address as source address.
                 * 
                 * During promiscuous mode, packets that were sent to us,
                 * should regain the destination MAC address of the watched host.
                 */
                if (!Device.PhysicalAddress.Equals(PhysicalAddress.None))
                {
                    copy.SourceHardwareAddress = Device.PhysicalAddress; // always use own MAC address as source

                    if (copy.DestinationHardwareAddress?.Equals(Device.PhysicalAddress) ?? false)
                    {
                        copy.DestinationHardwareAddress = Host.PhysicalAddress; // replace the spoofed MAC address
                    }
                }

                Device.SendPacket(copy);
            }
        }

        private void EndRequest(DemandRequest request)
        {
            foreach (var pair  in _ongoingRequests)
            {
                if (pair.Value == request)
                {
                    _ongoingRequests.TryRemove(pair.Key, out _);

                    break;
                }
            }
        }
    }
}
