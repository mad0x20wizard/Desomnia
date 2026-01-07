using NLog;
using NLog.Config;

namespace MadWizard.Desomnia
{
    public class ConfigDetector
    {
        const string CONFIG_FILE_NAME = "monitor.xml";
        const string NLOG_CONFIG_FILE_NAME = "NLog.config";

        readonly List<string> Paths = [];

        public ConfigDetector(params string[] paths)
        {
            Paths.Add(Directory.GetCurrentDirectory());
            Paths.Add(Path.Combine(Directory.GetCurrentDirectory(), "config"));

            Paths.AddRange(paths);
        }

        public string Lookup()
        {
            string configPath;
            string configNLogPath;

            foreach (var path in Paths)
            {
                configPath = Path.Combine(path, CONFIG_FILE_NAME);
                configNLogPath = Path.Combine(path, NLOG_CONFIG_FILE_NAME);

                if (Path.Exists(configNLogPath))
                {
                    LogManager.Configuration = new XmlLoggingConfiguration(configNLogPath);
                }

                if (Path.Exists(configPath))
                {
                    return configPath;
                }
            }

            return CONFIG_FILE_NAME;
        }
    }
}
