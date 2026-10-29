using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class ServiceFilterRule : IPFilterRule<IIPServiceFilterContext>
    {
        public required IPService Service
        {
            get; init
            {
                if (value.Protocol is not (IPProtocol.TCP or IPProtocol.UDP))
                    throw new ArgumentException("A service filter requires a single TCP or UDP protocol.", nameof(value));

                field = value;
            }
        }

        override public bool Matches(IIPServiceFilterContext context)
        {
            if (context.Service == Service)
            {
                return base.Matches(context);
            }

            return false;
        }
    }
}
