using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class HostFilterRule : PacketFilterRule
    {
        public abstract bool MatchesAddress(IPAddress? ip = null);

        override public bool Matches(EthernetPacket packet)
        {
            return MatchesAddress(packet.FindSourceIPAddress());
        }
    }

    public class StaticHostFilterRule(IEnumerable<IPAddress> addresses) : HostFilterRule
    {
        public override bool MatchesAddress(IPAddress? ip = null)
        {
            return ip != null && addresses.Contains(ip);
        }
    }

    public class DynamicHostFilterRule(NetworkHost host) : HostFilterRule
    {
        public override bool MatchesAddress(IPAddress? ip = null)
        {
            return ip != null && host.HasAddress(ip: ip);
        }
    }
}
