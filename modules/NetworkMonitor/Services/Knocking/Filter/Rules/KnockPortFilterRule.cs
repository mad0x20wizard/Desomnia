using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Knocking.Filter.Rules
{
    internal class KnockPortFilterRule(IPPort port) : KnockFilterRule
    {
        public KnockPortFilterRule(IPProtocol protocol, ushort port) : this(new(protocol, port)) { }

        public override bool Matches(IPPacket packet, KnockEvent knock)
        {
            if (knock.TargetPort is IPPort)
            {
                return knock.TargetPort.Equals(port);
            }

            return false;
        }
    }
}
