using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using System.Diagnostics;

namespace MadWizard.Desomnia
{
    public class ApplicationBuilder
    {
        readonly HostApplicationBuilder _builder;

        readonly List<Module> _modules = [];

        protected virtual string DefaultLogsPath => "${currentdir:dir=logs}";
        protected virtual string[] DefaultPluginsPaths => ["plugins"];

        public ApplicationBuilder()
        {
            _builder = new HostApplicationBuilder();

            _builder.Services.AddSingleton<IHostLifetime, ConsoleLifetime>();

            _builder.ConfigureContainer(new AutofacServiceProviderFactory(), ConfigureContainer);

            ConfigureLogging();
        }

        protected virtual void ConfigureLogging()
        {
            _builder.Logging.ClearProviders();

            _builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);

            // Fallback if no config file has been found
            if (LogManager.Configuration is LoggingConfiguration config)
            {
                if (!config.Variables.ContainsKey("logDir"))
                {
                    config.Variables["logDir"] = "${currentdir:dir=logs}";
                }
            }
            else
            {
                config = new LoggingConfiguration();
            }

            LogManager.ConfigurationChanged += (sender, args) =>
            {
                if (args.ActivatedConfiguration is LoggingConfiguration configNew && !configNew.HasConsoleTarget())
                {
                    var target = new ConsoleTarget("console")
                    {
                        Layout = "${pad:padding=5:inner=${level:uppercase=true}} :: ${message} ${exception}"
                    };

                    configNew.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, target, "MadWizard.Desomnia.*");

                    LogManager.Configuration = configNew;
                }
            };

            LogManager.Configuration = config;

            _builder.Logging.AddNLog();
        }

        protected void ConfigureContainer(ContainerBuilder container)
        {
            foreach (var module in _modules)
            {
                container.RegisterModule(module);
            }
        }

        public void RegisterModule(Module module)
        {
            _modules.Add(module);
        }

        public void RegisterPluginModules()
        {
            foreach(var path in DefaultPluginsPaths)
            {
                this.RegisterPluginModules(path);
            }
        }

        public void LoadConfiguration(string path)
        {
            var source = new ExtendedXmlConfigurationSource(path, optional: false);

            foreach (var module in _modules.OfType<ConfigurableModule>())
            {
                module.ConfigureConfigurationSource(source);
            }

            _builder.Configuration.Sources.Add(source);

            //_builder.AddCommandLine(args);
        }

        public IHost Build()
        {
            foreach (var module in _modules)
            {
                module.Build(_builder);
            }

            return _builder.Build();
        }
    }

    public class HomebrewApplicationBuilder : ApplicationBuilder
    {
        const string HOMEBREW_CONFIG_PATH = "/etc/desomnia";

        const string HOMEBREW_LOGS_PATH = "/log/desomnia";

        const string HOMEBREW_CORE_PLUGINS_PATH = "/opt/desomnia/lib/plugins";
        const string HOMEBREW_USER_PLUGINS_PATH = "/var/lib/desomnia/plugins";

        private static string Prefix
        {
            get
            {
                // First try environment variable (fastest)
                var envPrefix = Environment.GetEnvironmentVariable("HOMEBREW_PREFIX");
                if (!string.IsNullOrWhiteSpace(envPrefix) && Directory.Exists(envPrefix))
                    return envPrefix;

                // Fallback: execute `brew --prefix`
                var psi = new ProcessStartInfo
                {
                    FileName = "brew",
                    Arguments = "--prefix",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start brew process.");
                process.WaitForExit();

                string prefix = process.StandardOutput.ReadToEnd().Trim();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(prefix))
                    throw new InvalidOperationException("Unable to determine Homebrew prefix.");

                return prefix;
            }
        }

        public static string? ConfigPath
        {
            get
            {
                try
                {
                    return Prefix + HOMEBREW_CONFIG_PATH;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        protected override string DefaultLogsPath => Prefix + HOMEBREW_LOGS_PATH;
        protected override string[] DefaultPluginsPaths => [Prefix + HOMEBREW_CORE_PLUGINS_PATH, Prefix + HOMEBREW_USER_PLUGINS_PATH];

    }
}