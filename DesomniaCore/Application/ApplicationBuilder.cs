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

        protected virtual string DefaultLogsPath
        {
            get
            {
                if (Environment.GetEnvironmentVariable("DESOMNIA_LOGS_DIR") is string logs)
                    return logs;

                return "${currentdir:dir=logs}";
            }
        }

        protected virtual string[] DefaultPluginsPaths
        {
            get
            {
                List<string> paths = [];

                if (Environment.GetEnvironmentVariable("DESOMNIA_PLUGINS_DIR") is string plugins)
                    paths.Add(plugins);
                if (Environment.GetEnvironmentVariable("DESOMNIA_CORE_PLUGINS_DIR") is string core)
                    paths.Add(core);
                if (Environment.GetEnvironmentVariable("DESOMNIA_USER_PLUGINS_DIR") is string user)
                    paths.Add(user);

                return paths.Count > 0 ? [.. paths] : ["plugins"];
            }
        }

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
}