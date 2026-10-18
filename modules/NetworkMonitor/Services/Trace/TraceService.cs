using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;

namespace MadWizard.Desomnia.Network.Trace
{
    internal class TraceService(TraceService.Options options) : INetworkService, IDevicePacketFilter
    {
        public required ILogger<TraceService> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        public IEnumerable<NetworkHost> Hosts => Network.Where(host => options.Hosts.Contains(host.Name));

        async Task INetworkService.Startup()
        {
            Logger.LogDebug("Tracing network hosts:");

            foreach (var host in Hosts)
            {
                if (host.PhysicalAddress == null)
                    Logger.LogWarning($"{host.Name} has no MAC address");
                else
                    Logger.LogDebug(host.Name);
            }
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            if (FindHost(packet) is NetworkHost host)
            {
                using var scope = Logger.BeginHostScope(host);

                Logger.LogTrace($"RECEIVED PACKET\n{packet.ToTraceString()}");
            }
        }

        bool IDevicePacketFilter.FilterIncoming(PacketCapture packet) => false;

        bool IDevicePacketFilter.FilterOutgoing(Packet packet)
        {
            if (FindHost(packet) is NetworkHost host)
            {
                using var scope = Logger.BeginHostScope(host);

                Logger.LogTrace($"SEND PACKET\n{packet.ToTraceString()}");
            }

            return false;
        }

        private NetworkHost? FindHost(Packet packet)
        {
            if (packet is EthernetPacket ethernet)
            {
                if (Hosts.FirstOrDefault(host => ethernet.SourceHardwareAddress.Equals(host.PhysicalAddress)) is NetworkHost host)
                {
                    return host;
                }
            }

            return null;
        }

        internal readonly struct Options
        {
            public string[] Hosts { get; init; }
        }
    }
}
