using MadWizard.Desomnia.Network.Neighborhood;
using NetTools;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class HostRangeFilterRule : HostFilterRule { }

    public class StaticHostRangeFilterRule : HostRangeFilterRule
    {
        public required IPAddressRange Range { get; init; }

        public override bool MatchesAddress(IPAddress? ip = null)
        {
            return ip != null && Range.Contains(ip);
        }
    }

    public class DynamicHostRangeFilterRule : HostRangeFilterRule, IDisposable
    {
        public required NetworkHostRange Range
        {
            get; init
            {
                (field = value).AddDynamicFilterRule(this);
            }
        }

        public override bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            if (base.Matches(packet, direction))
            {
                if (Range is NetworkServiceHostRange range)
                {
                    return MatchesRules(range.Rules, packet, direction);
                }

                return true;
            }

            return false;
        }

        public override bool MatchesAddress(IPAddress? ip = null)
        {
            return ip != null && Range.Contains(ip);
        }

        void IDisposable.Dispose()
        {
            Range.RemoveDynamicFilterRule(this);
        }
    }
}
