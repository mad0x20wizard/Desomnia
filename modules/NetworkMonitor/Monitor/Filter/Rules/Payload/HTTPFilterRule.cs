namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class HTTPFilterRule : TCPServiceFilterRule
    {
        public HTTPFilterRule(ushort port) : base(port)
        {

        }

        public List<HTTPRequestFilterRule> RequestRules { get; set; } = [];


        protected override bool MatchesPayload(byte[] payload)
        {
            return true;
        }
    }
}
