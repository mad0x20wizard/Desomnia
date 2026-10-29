using Autofac;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Filter;
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

        public required ILifetimeScope          Scope                   { protected get; init; }

        public required NetworkDevice           Device                  { protected get; init; }
        public required NetworkSegment          Network                 { protected internal get; init; }

        public required AdvertiseOptions        AdvertiseOptions        { get; init; }
        public required HandoffOptions          HandoffOptions          { get; init; }
        public required DemandOptions           DemandOptions           { get; init; }

        public required IFilterContextParser    Parser                  { private get; init; }
        public required Lazy<IFilter>           Filter                  { internal get; init; }

        public bool AwaitServices { get => TrackingCount == 0 && field; set; }
        public virtual bool WatchServices { get => TrackingCount > 0 || AwaitServices; }

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

        internal DemandRequest? Evaluate(INetworkCapture capture)
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
                    ReportCapturedTraffic(capture);
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

        private protected virtual bool ShouldStartRequest(INetworkCapture capture)
        {
            try
            {
                var context = Parser.CreateContext(new(ref capture));

                if (Verify(context))
                {
                    if (CanTriggerDemand(capture))
                        return true;

                    base.ReportNetworkTraffic(context);

                    return false;
                }

                return false; // shortcut successfully taken
            }
            catch (AdditionalDataNeededException)
            {
                return true; // to gather more information, we now need to start a request
            }
            catch (MalformedPayloadException)
            {
                return false;
            }
        }


        public bool Verify(IFilterContext context)
        {
            // A selected service owns its policy, including inherited host rules.
            // Address resolution can request transport information after host-only vetoes.
            if (WatchServices)
            {
                if (context is IIPServiceFilterContext ips)
                {
                    if (this[Host[ips.Service]] is NetworkServiceWatch watch)
                    {
                        return !watch.Filter.Value.ShouldFilter(context);
                    }

                    return false; // does not match any tracked service
                }

                // can we decline the request by host rules only?
                else if (context is not IIPFilterContext && context.IsInbound && !Filter.Value.ShouldFilter(context))
                {
                    throw new IPTrafficNeededException();
                }
                else
                {
                    return false;
                }
            }
            else // Host only monitoring
            {
                return !Filter.Value.ShouldFilter(context);
            }
        }

        protected internal override void ReportNetworkTraffic(IFilterContext context)
        {
            try
            {
                if (Verify(context))
                {
                    base.ReportNetworkTraffic(context);
                }
            }
            catch (AdditionalDataNeededException)
            {
                // Accounting never starts a request to gather missing information.
            }
            catch (MalformedPayloadException)
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

            if (request.FilterContext is IIPServiceFilterContext ips)
            {
                @event.Service = Host[ips.Service];
            }

            ReportNetworkTraffic(@event);

            await TriggerDemandAsync(@event);

            if (this[@event.Service] is NetworkServiceWatch watch)
            {
                await watch.TriggerDemandAsync(@event);
            }
        }

        protected void ReportNetworkTraffic(DemandEvent @event)
        {
            foreach (var capture in @event)
            {
                ReportCapturedTraffic(capture);
            }
        }

        private void ReportCapturedTraffic(INetworkCapture capture)
        {
            try
            {
                ReportNetworkTraffic(Parser.CreateContext(new(ref capture)));
            }
            catch (AdditionalDataNeededException)
            {
                // Incomplete application headers cannot start a request while accounting.
            }
            catch (MalformedPayloadException)
            {
                // Parsing can reject traffic before a filter context exists.
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
