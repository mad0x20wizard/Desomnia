using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Neighborhood;
using NLog;
using NLog.LayoutRenderers;
using System.Net;
using System.Text;

namespace MadWizard.Desomnia.Network.Logging
{
    [LayoutRenderer("host")]
    public class NetworkHostLayoutRenderer : LayoutRenderer
    {
        public bool WithSource { get; set; }
        public bool WithRequest { get; set; }

        protected override void Append(StringBuilder sb, LogEventInfo logEvent)
        {
            var scope = ScopeContext.GetAllProperties();

            if (scope.FirstOrDefault(p => p.Key == "Host").Value is NetworkHost host)
            {
                sb.Append(host.Name);

                if (WithSource)
                {
                    if (scope.FirstOrDefault(p => p.Key == "Source").Value is NetworkHost sourceHost)
                        sb.Append("/" + sourceHost.Name);
                    if (scope.FirstOrDefault(p => p.Key == "Source").Value is IPAddress sourceIP)
                        sb.Append("/" + sourceIP.ToString().Replace(":", "."));
                }

                if (WithRequest)
                {
                    if (scope.FirstOrDefault(p => p.Key == "Request").Value is DemandRequest request)
                        sb.Append("/" + request.Number);
                }
            }
        }
    }
}