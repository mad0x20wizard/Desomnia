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

        internal static void Run(XDocument config)
        {
            foreach (var element in config.Descendants())
            {
                element.AttributeNamed(LEGACY_MAX_IDLE_TIME_ATTRIBUTE)?.MigrateRename(MAX_LAST_INPUT_TIME_ATTRIBUTE);
                element.AttributeNamed(LEGACY_CLOCK_TIME_ATTRIBUTE)?.MigrateRename(WATCH_INPUT_ATTRIBUTE);
                element.AttributeNamed(LEGACY_CLOCK_REMOTE_ATTRIBUTE)?.MigrateRename(WATCH_INPUT_REMOTE_ATTRIBUTE);
                element.AttributeNamed(LEGACY_CLOCK_DISCONNECTED_ATTRIBUTE)?.MigrateRename(WATCH_INPUT_DISCONNECTED_ATTRIBUTE);
            }
        }
    }
}
