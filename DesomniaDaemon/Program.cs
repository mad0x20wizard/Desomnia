using Autofac;
using CommandLine;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Daemon.Options;
using MadWizard.Desomnia.Daemon.Tools;
using MadWizard.Desomnia.Logging;
using Microsoft.Extensions.Hosting;
using NLog;

//await MadWizard.Desomnia.Test.Debugger.UntilAttached();

LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration")); // FIXME

const string FHS_CONFIG_PATH = "/etc/desomnia"; // Filesystem Hierarchy Standard

bool autoReload = false;
string? autoReloadPath = null;
Parser.Default.ParseArguments<CommandLineOptions>(args)
    .WithParsed(options =>
    {
        autoReload = options.AutoReload;
        autoReloadPath = options.AutoReloadPath;
    })
    .WithNotParsed(errors =>
    {
        Environment.Exit(1);
    });

string configPath = new ConfigDetector(FHS_CONFIG_PATH).Lookup();

try
{
    if (!UnixHelper.IsRoot)
        throw new Exception("The application must be run with root privileges.");

    ConfigFileWatcher watcher;

    do
    {
        using (new SystemMutex("MadWizard.Desomnia", true)) using (watcher = new(autoReloadPath ?? configPath) { EnableRaisingEvents = autoReload })
        {
            var builder = new DesomniaDaemonBuilder(useFHS: configPath.StartsWith(FHS_CONFIG_PATH));

            builder.RegisterModule<MadWizard.Desomnia.CoreModule>();
            builder.RegisterModule<MadWizard.Desomnia.Daemon.PlatformModule>();
            builder.RegisterModule<MadWizard.Desomnia.Network.Module>();
            builder.RegisterModule<MadWizard.Desomnia.Network.FirewallKnockOperator.PluginModule>();

            builder.LoadConfiguration(configPath);

            builder.Build().RunAsync(watcher.Token).Wait();
        }
    }
    while (watcher.HasChanged);

    return 0;
}
catch (Exception)
{
    throw;
}

class DesomniaDaemonBuilder(bool useFHS = false) : MadWizard.Desomnia.ApplicationBuilder
{
    const string FHS_LOGS_PATH = "/var/log/desomnia";

    protected override string DefaultLogsPath => useFHS ? FHS_LOGS_PATH : base.DefaultLogsPath;
}