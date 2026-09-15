using MadWizard.Desomnia.Configuration.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Session.Configuration.Migration
{
    /// <summary>
    /// Version 2 renamed the attributes that configure session input-idleness tracking.
    /// </summary>
    internal static class V2
    {
        const string LEGACY_MAX_IDLE_TIME_ATTRIBUTE          = "maxIdleTime";
        const string MAX_LAST_INPUT_TIME_ATTRIBUTE           = "maxLastInputTime";
        const string LEGACY_CLOCK_TIME_ATTRIBUTE             = "clockTime";
        const string WATCH_INPUT_ATTRIBUTE                   = "watchInput";
        const string LEGACY_CLOCK_REMOTE_ATTRIBUTE           = "clockRemote";
        const string WATCH_INPUT_REMOTE_ATTRIBUTE            = "watchInputRemote";
        const string LEGACY_CLOCK_DISCONNECTED_ATTRIBUTE     = "clockDisconnected";
        const string WATCH_INPUT_DISCONNECTED_ATTRIBUTE      = "watchInputDisconnected";
        const string LEGACY_DEMAND_ATTRIBUTE                 = "onDemand";
        const string USAGE_ATTRIBUTE                         = "onUsage";
        const string LEGACY_SESSION_DEMAND_ATTRIBUTE         = "onSessionDemand";
        const string SESSION_USAGE_ATTRIBUTE                 = "onSessionUsage";

        internal static void Run(XDocument config)
        {
            foreach (var monitor in config.DescendantsNamed("SessionMonitor"))
            {
                monitor.AttributeNamed(LEGACY_DEMAND_ATTRIBUTE)?.MigrateRename(USAGE_ATTRIBUTE);
            }

            foreach (var element in config.Descendants())
            {
                element.AttributeNamed(LEGACY_MAX_IDLE_TIME_ATTRIBUTE)?.MigrateRename(MAX_LAST_INPUT_TIME_ATTRIBUTE);
                element.AttributeNamed(LEGACY_CLOCK_TIME_ATTRIBUTE)?.MigrateRename(WATCH_INPUT_ATTRIBUTE);
                element.AttributeNamed(LEGACY_CLOCK_REMOTE_ATTRIBUTE)?.MigrateRename(WATCH_INPUT_REMOTE_ATTRIBUTE);
                element.AttributeNamed(LEGACY_CLOCK_DISCONNECTED_ATTRIBUTE)?.MigrateRename(WATCH_INPUT_DISCONNECTED_ATTRIBUTE);
                element.AttributeNamed(LEGACY_SESSION_DEMAND_ATTRIBUTE)?.MigrateRename(SESSION_USAGE_ATTRIBUTE);
            }
        }
    }
}
