using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Demand
{
    internal class DemandService : INetworkService
    {
        public required ILogger<DemandService> Logger { private get; init; }

        public required NetworkDevice   Device  { private get; init; }
        public required NetworkMonitor  Monitor { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        public required AddressMappingService AddressMapping { private get; init; }

        public required IEnumerable<IDemandDetector> Detectors { private get; init; }

        private bool IsWatchedBy<T>(in CaptureSummary packet) where T : NetworkHostWatch
        {
            if ((Network[packet.TargetAddress] ?? Network[packet.TargetPhysicalAddress]) is NetworkHost host)
            {
                return Monitor[host] is T;
            }

            return false;
        }

        private bool ShouldProcess(in CaptureSummary packet)
        {
            switch (Monitor.Options.Mode)
            {
                default:
                case WatchMode.Normal:
                    return Device.HasSentPacket(packet.SourcePhysicalAddress) || IsWatchedBy<LocalHostWatch>(packet);

                case WatchMode.Promiscuous:
                    return true;
            }
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            var summary = new CaptureSummary(packet);

            if (ShouldProcess(summary))
            {
                ReportSourceTraffic(summary);

                foreach (var detector in Detectors)
                {
                    if (detector.Examine(summary) is NetworkHost host)
                    {
                        if (Monitor[host] is HostDemandWatch watch)
                        {
                            EvaluateDemand(watch, summary); break;
                        }
                    }
                }
            }
        }

        /**
         * Attributes the packet to the watch of its SOURCE host — outbound accounting
         * for the local host and any other host whose traffic passes our capture point
         * (bridged VMs). A counting-only lane: demand evaluation stays strictly
         * destination-keyed, so wake/verify/forward logic never sees a packet from
         * the watched host's own side.
         */
        private void ReportSourceTraffic(in CaptureSummary packet)
        {
            if (packet.Extract<IPPacket>() is not null)
            {
                if (Network[packet.SourceAddress] is NetworkHost host && Monitor[host] is NetworkHostWatch watch)
                {
                    watch.ReportNetworkTraffic(packet.Ethernet, PacketDirection.Outbound);
                }
            }
        }

        private void EvaluateDemand(HostDemandWatch watch, in CaptureSummary capture)
        {
            if (watch.Evaluate(capture) is DemandRequest request)
            {
                using var scope = Logger.BeginHostScope(watch.Host);

                using (ExecutionContext.SuppressFlow()) // we want to establish a new request context
                {
                    Task.Run(async () => await ExecuteDemandRequest(watch, request));
                }
            }
        }

        private async Task ExecuteDemandRequest(HostDemandWatch watch, DemandRequest request)
        {
            using var scope = Logger.BeginRequestScope(watch, request);

            using (request)
            {
                bool forward = true;

                await foreach (var packet in request.ReadPackets(watch.DemandOptions.Timeout))
                {
                    using var scopePacket = Logger.BeginRequestPacketScope(packet);

                    try
                    {
                        if (watch.Verify(packet))
                        {
                            await watch.ReportDemand(request, forward); // TODO: discard remaining packets

                            request.Result ??= DemandResult.Valid;
                        }
                    }
                    catch (IPUnicastNeededException needed)
                    {
                        if (!watch.IsOnline)
                        {
                            if (watch.AdvertiseOptions.ShouldAdvertiseOnRemoteHostDemand(needed.Address))
                            {
                                Logger.LogTrace($"More information needed; try to request IP unicast traffic");

                                if (await watch.RequestIPUnicastTrafficTo(needed.Address) is PhysicalAddress mac)
                                {
                                    AddressMapping.Advertise(new(needed.Address, mac), respondTo: packet);

                                    continue;
                                }
                            }
                        }
                    }
                    catch (ServicePayloadNeededException needed)
                    {
                        if (watch.IsOnline)
                        {
                            continue; // the host will accept the connection by itself

                            // TODO: ConnectionService: watch in passive mode
                        }
                        else
                        {
                            if (needed.Port.Protocol.HasFlag(IPProtocol.TCP)) // we need to answer on behalf of the watched host
                            {
                                forward = false;

                                // TODO: ConnectionService: send SYN and watch in active mode

                                throw new NotImplementedException(); // LATER: Implement payload filters
                            }

                        }
                    }

                    request.Result ??= DemandResult.Filtered;

                    break;
                }

                request.Result ??= DemandResult.Timeout;
            }
        }
    }
}
