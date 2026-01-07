using Autofac;
using MadWizard.Desomnia;
using MadWizard.Desomnia.LaunchDaemon.Tools;
using MadWizard.Desomnia.Logging;
using Microsoft.Extensions.Hosting;
using NLog;

//await MadWizard.Desomnia.Test.Debugger.UntilAttached();

LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration")); // FIXME

const string AFS_CONFIG_PATH = "/Library/Application Support/Desomnia/config"; // Apple Filesystem Standard

string configPath = new ConfigDetector(AFS_CONFIG_PATH).Lookup();

try
{
    if (!MacOSHelper.IsRoot)
        throw new Exception("The application must be run with root privileges.");

    ConfigFileWatcher watcher;

    do
    {
        using (new SystemMutex("MadWizard.Desomnia", true)) using (watcher = new(configPath) { EnableRaisingEvents = false })
        {
            var builder = new DesomniaLaunchDaemonBuilder(useAFS: configPath.StartsWith(AFS_CONFIG_PATH));

            builder.RegisterModule<MadWizard.Desomnia.CoreModule>();
            builder.RegisterModule<MadWizard.Desomnia.LaunchDaemon.PlatformModule>();
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

class DesomniaLaunchDaemonBuilder(bool useAFS = false) : MadWizard.Desomnia.ApplicationBuilder
{
    const string AFS_LOGS_PATH = "/Library/Logs/Desomnia";

    protected override string DefaultLogsPath => useAFS ? AFS_LOGS_PATH : base.DefaultLogsPath;
}