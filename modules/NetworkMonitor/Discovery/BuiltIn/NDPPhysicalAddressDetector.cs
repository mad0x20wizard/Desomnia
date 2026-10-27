using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class NDPPhysicalAddressDetector : PhysicalAddressDetector<NDPPhysicalAddressDetector>
    {
        public required override ILogger<NDPPhysicalAddressDetector> Logger { protected get; init; }

        protected override AddressFamily Family => AddressFamily.InterNetworkV6;

        protected override void SendRequest(IPAddress ip) => Reachability.SendNDPNeighborSolicitation(ip);

        protected override AddressMapping? AnalyzeCapture(INetworkCapture packet)
        {
            if (packet.FindPacket<NdpPacket>() is NdpNeighborAdvertisementPacket ndp)
            {
                if (!ndp.TargetAddress.IsEmpty() && ndp.FindSourcePhysicalAddress() is PhysicalAddress mac)
                {
                    return new(ndp.TargetAddress, mac);
                }
            }

            return null;
        }
    }
}
