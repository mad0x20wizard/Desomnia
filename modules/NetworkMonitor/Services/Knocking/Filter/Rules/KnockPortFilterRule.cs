using System.Net;

namespace MadWizard.Desomnia.Network.Knocking.Filter.Rules
{
    internal class KnockPortFilterRule(IPService port) : KnockFilterRule
    {
        public KnockPortFilterRule(IPProtocol protocol, ushort port) : this(new(protocol, port)) { }

        public override bool Matches(IPEndPoint source, KnockEvent knock)
        {
            if (knock.TargetPort is IPService)
            {
                return knock.TargetPort.Equals(port);
            }

            return false;
        }
    }
}
