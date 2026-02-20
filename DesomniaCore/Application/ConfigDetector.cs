using NLog;
using NLog.Config;
using System.IO;

namespace MadWizard.Desomnia
{
    public class PathDetector
    {
        protected readonly List<string> Paths = [];

        public PathDetector(params string[] paths)
        {
            Paths.AddRange(paths);
        }

        public virtual string? Lookup()
        {
            foreach (var path in Paths)
            {
                if (Path.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            return null;
        }
    }

    public class ConfigDetector : PathDetector
    {
        const string CONFIG_FILE_NAME = "monitor.xml";
        const string NLOG_CONFIG_FILE_NAME = "NLog.config";

        readonly PathDetector _nlogConfigDetector;

        public ConfigDetector(params string[] paths) : base([])
        {
            if (Environment.GetEnvironmentVariable("DESOMNIA_CONFIG_DIR") is string config)
                paths = [.. paths, config];

            var basePaths = new List<string>(paths)
            {
                Path.Combine(Directory.GetCurrentDirectory(), "config"),
                Directory.GetCurrentDirectory(),
            };

            basePaths.Reverse();

            Paths.AddRange(basePaths.Select(p => Path.Combine(p, CONFIG_FILE_NAME)));

            _nlogConfigDetector = new PathDetector([.. basePaths.Select(p => Path.Combine(p, NLOG_CONFIG_FILE_NAME))]);
        }

        public override string Lookup()
        {
            if (_nlogConfigDetector.Lookup() is string configNLogPath)
            {
                LogManager.Configuration = new XmlLoggingConfiguration(configNLogPath);
            }

            return base.Lookup() ?? CONFIG_FILE_NAME;
        }
    }
}
