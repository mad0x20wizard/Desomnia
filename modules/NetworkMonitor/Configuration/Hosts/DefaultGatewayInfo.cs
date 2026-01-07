using MadWizard.Desomnia.Network.Configuration.Options;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    internal class DefaultGatewayInfo(NetworkInterface ni, AutoDiscoveryType auto) : NetworkRouterInfo
    {
        internal async Task TryLookupGatewayName()
        {
            foreach (var ip in IPAddresses)
            {
                if (await ip.LookupName() is string name)
                {
                    Name = name;
                    AutoDetect = auto;
                    break;
                }
            }
        }

        public override IEnumerable<IPAddress> IPAddresses
        {
            get
            {
                foreach (var gateway in ni.GetIPProperties().GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork)
                        if (auto.HasFlag(AutoDiscoveryType.IPv4))
                            yield return gateway.Address;

                    if (gateway.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        if (auto.HasFlag(AutoDiscoveryType.IPv6))
                        {
                            gateway.Address.ScopeId = 0; // ignore _scope id
                            yield return gateway.Address;
                        }
                }
            }
        }
    }
}
