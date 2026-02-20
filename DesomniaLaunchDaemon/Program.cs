using Autofac;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Logging;
using Microsoft.Extensions.Hosting;
using NLog;

//await MadWizard.Desomnia.Test.Debugger.UntilAttached();

LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration")); // FIXME

string configPath = new ConfigDetector(HomebrewApplicationBuilder.ConfigPath).Lookup();

try
{
    if (!Environment.IsPrivilegedProcess)
        throw new Exception("The application must be run with root privileges.");

    ConfigFileWatcher watcher;

    do
    {
        using (new SystemMutex("MadWizard.Desomnia", true)) using (watcher = new(configPath) { EnableRaisingEvents = false })
        {
            MadWizard.Desomnia.ApplicationBuilder builder =
                HomebrewApplicationBuilder.ConfigPath is string homebrew && configPath.StartsWith(homebrew) 
                    ? new MadWizard.Desomnia.HomebrewApplicationBuilder() 
                    : new MadWizard.Desomnia.ApplicationBuilder();

            builder.RegisterModule<MadWizard.Desomnia.CoreModule>();
            builder.RegisterModule<MadWizard.Desomnia.LaunchDaemon.PlatformModule>();
            builder.RegisterModule<MadWizard.Desomnia.Network.Module>();

            builder.RegisterPluginModules();

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
