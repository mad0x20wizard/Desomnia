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
            sb.Append("network");
        }
    }
}
