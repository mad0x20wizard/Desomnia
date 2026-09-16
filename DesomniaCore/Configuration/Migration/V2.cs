using MadWizard.Desomnia.Configuration.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// Version 2 separated periodic usage inspection from externally triggered demand.
    /// </summary>
    internal static class V2
    {
        const string LEGACY_DEMAND_ATTRIBUTE = "onDemand";
        const string USAGE_ATTRIBUTE         = "onUsage";

        internal static void Run(XDocument config)
        {
            if (config.Root is null)
                return;

            foreach (var monitor in config.Root.DescendantsAndSelf().Where(element => element.HasLocalName("SystemMonitor")))
            {
                monitor.AttributeNamed(LEGACY_DEMAND_ATTRIBUTE)?.MigrateRename(USAGE_ATTRIBUTE);
            }
        }
    }
}
