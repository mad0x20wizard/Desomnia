using MadWizard.Desomnia.Configuration.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Processes.Configuration.Migration
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
            foreach (var element in config.DescendantsNamed("ProcessMonitor")
                .Concat(config.DescendantsNamed("Process")))
            {
                element.AttributeNamed(LEGACY_DEMAND_ATTRIBUTE)?.MigrateRename(USAGE_ATTRIBUTE);
            }
        }
    }
}
