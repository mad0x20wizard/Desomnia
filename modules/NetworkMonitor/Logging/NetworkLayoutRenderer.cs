using MadWizard.Desomnia.Network.Context;
using NLog;
using NLog.LayoutRenderers;
using System.Text;

namespace MadWizard.Desomnia.Network.Logging
{
    [LayoutRenderer("network")]
    public class NetworkLayoutRenderer : LayoutRenderer
    {
        protected override void Append(StringBuilder sb, LogEventInfo logEvent)
        {
            if (ScopeContext.TryGetProperty("Network", out var property) && property is NetworkContext network)
            {
                sb.Append(network.Name);
            }
        }
    }
}
