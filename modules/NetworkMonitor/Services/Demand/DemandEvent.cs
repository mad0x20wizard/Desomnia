using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Data;
using System.Net;

namespace MadWizard.Desomnia.Network.Demand
{
    public class DemandEvent(NetworkHost host, IPAddress? ip = null, IEnumerable<INetworkCapture>? packets = null) : Event("Demand"), IIEnumerable<INetworkCapture>
    {
        public INetworkCapture TriggerPacket => this.First();

        private INetworkCapture? TriggerIPCapture => this.FirstOrDefault(capture => capture.IsIPUnicast);

        public NetworkHost Host { get; private init; } = host;
        public NetworkService? Service { get; set; }

        public IPAddress?   SourceAddress   => TriggerIPCapture?.SourceAddress;
        public IPEndPoint?  TargetEndPoint  => TriggerIPCapture?.TargetEndPoint ?? (ip is not null ? new IPEndPoint(ip, 0) : null);

        public bool CanBeForwarded { get; set; } = false;

        public DemandEvent(DemandRequest request) : this(request.Host, request.TargetAddress, request)
        {

        }

        public IEnumerator<INetworkCapture> GetEnumerator()
        {
            return packets?.GetEnumerator() ?? Enumerable.Empty<INetworkCapture>().GetEnumerator();
        }
    }
}
