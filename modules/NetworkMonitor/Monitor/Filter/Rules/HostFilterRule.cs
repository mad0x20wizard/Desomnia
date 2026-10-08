using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class HostFilterRule : PacketFilterRule
    {
        public abstract bool MatchesAddress(IPAddress? ip = null);

        override public bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            var ip = direction == PacketDirection.Inbound
                ? packet.FindSourceIPAddress()
                : packet.FindTargetIPAddress();

            return MatchesAddress(ip);
        }
    }

    public class StaticHostFilterRule : HostFilterRule
    {
        public required IEnumerable<IPAddress> Addresses { get; init; }

        public override bool MatchesAddress(IPAddress? ip = null)
        {
            return ip != null && Addresses.Contains(ip);
        }
    }

    public class DynamicHostFilterRule : HostFilterRule, IDisposable
    {
        public required NetworkHost Host
        {
            get; set
            {
                (field = value).FilterRefCount++;
            }
        }

        public override bool MatchesAddress(IPAddress? ip = null)
        {
            return ip != null && Host.HasAddress(ip: ip);
        }

        void IDisposable.Dispose()
        {
            Host.FilterRefCount--;
        }
    }
}
