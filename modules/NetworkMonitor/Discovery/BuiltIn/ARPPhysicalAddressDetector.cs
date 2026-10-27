using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Capture;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn 
{
    internal class ARPPhysicalAddressDetector : PhysicalAddressDetector<ARPPhysicalAddressDetector>
    {
        public required override ILogger<ARPPhysicalAddressDetector> Logger { protected get; init; }

        protected override AddressFamily Family => AddressFamily.InterNetwork;

        protected override void SendRequest(IPAddress ip) => Reachability.SendARPRequest(ip);

        protected override AddressMapping? AnalyzeCapture(INetworkCapture packet)
        {
            if (packet.HasPacket(out ArpPacket? arp))
            {
                if (arp.Operation == ArpOperation.Response)
                {
                    if (!arp.SenderProtocolAddress.IsEmpty() 
                        && !arp.SenderHardwareAddress.Equals(PhysicalAddress.None)
                        && !arp.SenderHardwareAddress.Equals(PhysicalAddressExt.Empty)
                        && !arp.SenderHardwareAddress.Equals(PhysicalAddressExt.Broadcast))

                        return new(arp.SenderProtocolAddress, arp.SenderHardwareAddress);
                }
            }

            return null;
        }
    }
}
