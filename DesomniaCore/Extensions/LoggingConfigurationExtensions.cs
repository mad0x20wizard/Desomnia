using NLog.Targets;

namespace NLog.Config
{
    public static class LoggingConfigurationExtensions
    {
        public static bool HasConsoleTarget(this LoggingConfiguration config)
        {
            foreach (var target in config.AllTargets)
            {
                if (target is ConsoleTarget consoleTarget)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
