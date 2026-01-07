using Autofac;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Demand.Filter;
using MadWizard.Desomnia.Network.Filter;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    public abstract class HostDemandWatch : NetworkHostWatch
    {
        private int _requestNr = 1;

        public abstract bool IsOnline { get; }

        protected bool HasOngoingRequests => !_ongoingRequests.IsEmpty;

        protected virtual int MaxConcurrentRequests => DemandOptions.Parallel;

        public required ILifetimeScope Scope { protected get; init; }

        public required NetworkDevice Device { protected get; init; }

        public required DemandOptions               DemandOptions   { get; init; }
        public required Lazy<IDemandPacketFilter>   DemandFilter    { private get; init; }

        readonly ConcurrentDictionary<IPAddress, DemandRequest> _ongoingRequests = [];

        internal protected virtual DemandRequest? Evaluate(EthernetPacket packet)
        {
            /**
             * Magic Packets should never start a demand request,
             * but shall be allowed to trigger special actions,
             * like waking up a virtual host immediately
             * or responding with an address advertisement in order
             * to signal the presence of the target host.
             */
            if (packet.IsMagicPacket())
            {
                HandleMagicPacket(packet);
            }

            /**
             * Only packets with a valid source Address address will be processed
             * and can trigger a demand request.
             */
            else if (packet.FindSourceIPAddress() is IPAddress source)
            {
                // do we already have an ongoing request for that source Address?
                if (_ongoingRequests.TryGetValue(source, out DemandRequest? ongoing))
                {
                    ongoing.EnqueuePacket(packet);
                }

                // can we start a new request for that source Address?
                else if (MaybeStartRequest(packet) is DemandRequest request)
                {
                    request.EnqueuePacket(packet);

                    return request;
                }
            }

            return null;
        }

        protected virtual void HandleMagicPacket(EthernetPacket packet)
        {
            _ = TriggerEventAsync(nameof(MagicPacket), new DemandEvent(Host, packets: [packet])
            {
                // TODO: is something missing?
            });
        }

        private bool CanTriggerDemand(EthernetPacket packet)
        {
            if (packet.Extract<TcpPacket>() is TcpPacket tcp)
            {
                if (tcp.Synchronize) // ongoing TCP connections never trigger demand requests
                {
                    bool hasServiceDemand = this.Any(service => service.CanTriggerDemand(packet));

                    return !IsOnline || hasServiceDemand;
                }
            }
            else if (!IsOnline)
            {
                return true;
            }

            return false;
        }

        protected virtual bool ShouldStartRequest(EthernetPacket packet)
        {
            try
            {
                if (Verify(packet))
                {
                    if (CanTriggerDemand(packet))
                        return true;

                    ReportNetworkTraffic(packet);

                    return false;
                }

                return false; // shortcut successfully taken
            }
            catch (AdditionalDataNeededException)
            {
                return true; // to gather more information, we now need to start a request
            }
        }

        private DemandRequest? MaybeStartRequest(EthernetPacket trigger)
        {
            if (ShouldStartRequest(trigger))
            {
                if (_ongoingRequests.Count >= MaxConcurrentRequests)
                    return null;

                ILifetimeScope scope = Scope.BeginLifetimeScope(MatchingScopeLifetimeTags.RequestLifetimeScopeTag);

                var request = scope.Resolve<DemandRequest>(TypedParameter.From(trigger));
                request.Number = _requestNr++;

                scope.CurrentScopeEnding += (sender, args) => EndRequest(request);

                _ongoingRequests[request.SourceAddress] = request;

                return request;
            }

            return null;
        }

        public bool Verify(EthernetPacket packet)
        {
            return !DemandFilter.Value.ShouldFilter(packet);
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

            foreach (var watch in this.Where(watch => request.Any(packet => watch.Service.Accepts(packet))))
            {
                @event.Service = watch.Service;

                ReportNetworkTraffic(@event);

                await TriggerDemandAsync(@event);
                await watch.TriggerDemandAsync(@event);

                break;
            }
        }

        protected void ForwardPackets(IEnumerable<EthernetPacket> packets, bool onlyIPUnicast = true)
        {
            if (onlyIPUnicast)
            {
                packets = packets.Where(packet => packet.IsIPUnicast());
            }

            Logger.LogTrace($"FORWARD {packets.Count()} packet(s) to '{Host.Name}':");

            foreach (EthernetPacket packet in packets)
            {
                var copy = packet.MakeCopy();

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
            _ongoingRequests.TryRemove(request.SourceAddress, out _);
        }
    }
}
