using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.DhcpV4;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    /**
     * This detector can make the passice detection of a successful wake more reliable.
     * It cannot be used to actively resolve remote IP addresses, though.
     */
    internal class DHCPRequestDetector : INetworkService
    {
        public required ILogger<DHCPRequestDetector> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        public required ReachabilityService Reachability { private get; init; }

        void INetworkService.ProcessCapture(INetworkCapture packet)
        {
            if (packet.HasPacket(out DhcpV4Packet? dhcp))
                if (dhcp.Operation == DhcpV4Operation.BootRequest)
                    if (Network[dhcp.ClientHardwareAddress] is NetworkHost host)
                        foreach (var option in dhcp.GetOptions())
                            switch (option)
                            {
                                case AddressRequestOption requestAddr when requestAddr.RequestedIP is IPAddress ip:
                                {
                                    using (Logger.BeginHostScope(host))
                                        Logger.LogTrace("Host '{HostName}' requested {Family} address '{IPAddress}'",
                                            host.Name, ip.ToFamilyName(), ip);

                                    if (host.HasAddress(ip: ip)) // only if the IP already belongs to the host
                                    {
                                        Reachability.Notify(host, packet.SourcePhysicalAddress, new IPEndPoint(ip, 0));
                                    }

                                    break;
                                }
                            }
        }
    }
}
