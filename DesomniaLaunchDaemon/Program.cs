using Autofac;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Logging;
using Microsoft.Extensions.Hosting;
using NLog;

//await MadWizard.Desomnia.Test.Debugger.UntilAttached();

LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration")); // FIXME

const string HOMEBREW_CONFIG_PATH = "/opt/homebrew/etc/desomnia"; // Homebrew config location

string configPath = new ConfigDetector(HOMEBREW_CONFIG_PATH).Lookup();

try
{
    if (!Environment.IsPrivilegedProcess)
        throw new Exception("The application must be run with root privileges.");

    ConfigFileWatcher watcher;

    do
    {
        using (new SystemMutex("MadWizard.Desomnia", true)) using (watcher = new(configPath) { EnableRaisingEvents = false })
        {
            var builder = new DesomniaLaunchDaemonBuilder(useHomebrew: configPath.StartsWith(HOMEBREW_CONFIG_PATH));

            builder.RegisterModule<MadWizard.Desomnia.CoreModule>();
            builder.RegisterModule<MadWizard.Desomnia.LaunchDaemon.PlatformModule>();
            builder.RegisterModule<MadWizard.Desomnia.Network.Module>();

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

class DesomniaLaunchDaemonBuilder(bool useHomebrew = false) : MadWizard.Desomnia.ApplicationBuilder
{
    const string HOMEBREW_LOGS_PATH = "/opt/homebrew/var/log/desomnia";

    const string HOMEBREW_CORE_PLUGINS_PATH = "/opt/homebrew/opt/desomnia/lib/plugins";
    const string HOMEBREW_USER_PLUGINS_PATH = "/opt/homebrew/var/lib/desomnia/plugins";

    protected override string DefaultLogsPath => useHomebrew ? HOMEBREW_LOGS_PATH : base.DefaultLogsPath;
    protected override string[] DefaultPluginsPaths => useHomebrew ? [HOMEBREW_CORE_PLUGINS_PATH, HOMEBREW_USER_PLUGINS_PATH] : base.DefaultPluginsPaths;
}
