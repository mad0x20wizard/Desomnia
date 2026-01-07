using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Impersonation;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Demand
{
    internal class DemandService : INetworkService
    {
        public required ILogger<DemandService> Logger { private get; init; }

        public required NetworkDevice   Device  { private get; init; }
        public required NetworkMonitor  Monitor { private get; init; }

        public required AddressMappingService AddressMapping { private get; init; }

        public required IEnumerable<IDemandDetector> Detectors { private get; init; }

        private bool ShouldProcess(EthernetPacket packet)
        {
            switch (Monitor.Options.Mode)
            {
                default:
                case WatchMode.None:
                    return false;

                case WatchMode.Normal:
                    return Device.HasSentPacket(packet) || Monitor.IsWatchedBy<LocalHostWatch>(packet);

                case WatchMode.Promiscuous:
                    return true;
            }
        }

        async void INetworkService.Resume()
        {
            await MaybeAdvertiseWatch();
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            if (ShouldProcess(packet))
            {
                foreach (var detector in Detectors)
                {
                    if (detector.Examine(packet) is NetworkHost host)
                    {
                        if (Monitor[host] is HostDemandWatch watch)
                        {
                            EvaluateDemand(watch, packet); break;
                        }
                    }
                }
            }
        }

        private void EvaluateDemand(HostDemandWatch watch, EthernetPacket trigger)
        {
            using var scope = Logger.BeginHostScope(watch.Host);

            Stopwatch stop = Stopwatch.StartNew();

            if (watch.Evaluate(trigger) is DemandRequest request)
            {
                using (ExecutionContext.SuppressFlow()) // we want to establish a new request context
                {
                    Task.Run(async () => await ExecuteDemandRequest(watch, request, stop.Elapsed));
                }
            }
        }

        private async Task ExecuteDemandRequest(HostDemandWatch watch, DemandRequest request, TimeSpan prepare = default)
        {
            using var scope = Logger.BeginHostScope(watch.Host);

            Logger.LogTrace($"BEGIN {request}: '{(request.SourceHost is NetworkHost source ? source.Name : request.SourceAddress)}' -> '{request.Host.Name}'; prepare = {Math.Round(prepare.TotalMilliseconds)} ms");

            using (request)
            {
                bool forward = true;

                await foreach (var packet in request.ReadPackets(watch.DemandOptions.Timeout))
                {
                    Logger.LogTrace($"VERIFY packet = \n{packet.ToTraceString()}");

                    try
                    {
                        if (watch.Verify(packet))
                        {
                            await watch.ReportDemand(request, forward); // TODO: discard remaining packets
                        }
                    }
                    catch (IPUnicastNeededException needed)
                    {
                        if (!watch.IsOnline)
                        {
                            if (watch.DemandOptions.ShouldAdvertiseOnRemoteHostDemand(needed.Address))
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

                    break;
                }
            }

            Logger.LogTrace($"END {request}; duration = {Math.Floor(request.Duration.TotalMilliseconds)} ms");
        }

        void INetworkService.Suspend()
        {
            try
            {
                MaybeYieldLocalWatch();
            }
            catch (PcapException ex)
            {
                Logger.LogError("Could not yield local watches: " + ex.Message);
            }
        }

        #region Lifecycle
        private async Task MaybeAdvertiseWatch()
        {
            foreach (var watch in Monitor.OfType<HostDemandWatch>())
            {
                using var scope = Logger.BeginHostScope(watch.Host);

                if (watch.Host.IPAddresses.Where(watch.DemandOptions.ShouldAdvertiseOnLocalHostResume) is var ips && ips.Any())
                {
                    Logger.LogDebug($"Resuming operation, taking ownership of watched IP addresses...");

                    foreach (var ip in ips)
                    {
                        if (await watch.RequestIPUnicastTrafficTo(ip) is PhysicalAddress mac)
                        {
                            AddressMapping.Advertise(new(ip, mac));
                        }
                    }
                }
            }
        }

        private void MaybeYieldLocalWatch()
        {
            if (Monitor.Options.Yield)
            {
                foreach (var watch in Monitor.OfType<LocalHostWatch>())
                {
                    using var scope = Logger.BeginHostScope(watch.Host);

                    watch.YieldWatch();
                }
            }
        }
        #endregion
    }
}
