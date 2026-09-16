using MadWizard.Desomnia.Configuration.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Duo.Configuration.Migration
{
    /// <summary>
    /// Version 2 renamed the plugin's element from &lt;DuoStreamMonitor&gt; to
    /// &lt;DuoSessionMonitor&gt; (a Duo instance is a session, not just a stream).
    /// </summary>
    internal static class V2
    {
        const string LEGACY_MONITOR_ELEMENT = "DuoStreamMonitor";
        const string MONITOR_ELEMENT        = "DuoSessionMonitor";
        const string LEGACY_DEMAND_ATTRIBUTE = "onDemand";
        const string USAGE_ATTRIBUTE         = "onUsage";

        internal static void Run(XDocument config)
        {
            foreach (var monitor in config.DescendantsNamed(LEGACY_MONITOR_ELEMENT).ToList())
            {
                monitor.MigrateRename(MONITOR_ELEMENT);
            }

            foreach (var monitor in config.DescendantsNamed(MONITOR_ELEMENT))
            {
                monitor.AttributeNamed(LEGACY_DEMAND_ATTRIBUTE)?.MigrateRename(USAGE_ATTRIBUTE);
            }
        }
    }
}
