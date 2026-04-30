using Autofac;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Logging;
using MadWizard.Desomnia.Network.Logging;
using MadWizard.Desomnia.Service;
using MadWizard.Desomnia.Service.Windows;
using Microsoft.Extensions.Hosting;
using NLog;
using System.Diagnostics;
using System.Reflection;

//await MadWizard.Desomnia.Test.Debugger.UntilAttached();

LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration")); // FIXME
LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<NetworkHostLayoutRenderer>()); // FIXME
LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<NetworkLayoutRenderer>()); // FIXME

if (Process.GetCurrentProcess().IsWindowsService() is bool isRunningAsService && isRunningAsService)
{
    Directory.SetCurrentDirectory(DesomniaServiceBuilder.ProgramDataDir);
}

string configPath = new ConfigDetector().Lookup();

const string EVENT_LOG_NAME = "Application";
const string EVENT_LOG_SOURCE = "Desomnia";

try
{
    if (!Environment.IsPrivilegedProcess)
        throw new NotSupportedException("The application must be run with elevated privileges.");

    if (!EventLog.SourceExists(EVENT_LOG_SOURCE))
    {
        EventLog.CreateEventSource(EVENT_LOG_SOURCE, EVENT_LOG_NAME);
    }

    ConfigFileWatcher watcher;

    do
    {
        using (new SystemMutex("MadWizard.Desomnia", true)) using (watcher = new(configPath) { EnableRaisingEvents = isRunningAsService })
        {
            var builder = new DesomniaServiceBuilder(isRunningAsService);

            if (isRunningAsService)
            {
                builder.RegisterModule<WindowsServiceModule>();
            }

            builder.RegisterModule<MadWizard.Desomnia.CoreModule>();

            builder.RegisterModule<MadWizard.Desomnia.Service.PlatformModule>();

            builder.RegisterModule<MadWizard.Desomnia.Network.Module>();
            builder.RegisterModule<MadWizard.Desomnia.NetworkSession.Module>();
            builder.RegisterModule<MadWizard.Desomnia.PowerRequest.Module>();
            builder.RegisterModule<MadWizard.Desomnia.Process.Module>();
            builder.RegisterModule<MadWizard.Desomnia.Session.Module>();

            builder.RegisterPluginModules();

            builder.LoadConfiguration(configPath);

            IHost host = builder.Build();

            var service = host.Services.GetService(typeof(WindowsService)) as WindowsService;

            host.RunAsync(watcher.Token).Wait();

            /*
             * Once the SCM has been notifed about the service stop, there is no turning back.
             * Therefore we must schedule the service to restart itself after having stopped gracefully.
             */
            if (watcher.HasChanged && service is not null)
            {
                EventLog.WriteEntry(EVENT_LOG_SOURCE, $"Configuration file changed. Restarting...",
                    EventLogEntryType.Information, eventID: 1234);

                service.ScheduleSelfRestart();

                return -1;
            }
        }
    }
    while (watcher.HasChanged);

    return 0;
}
catch (Exception ex)
{
    if (isRunningAsService)
    {
        try
        {
            EventLog.WriteEntry(EVENT_LOG_SOURCE, $"{ex}", EventLogEntryType.Error);

            return 1;
        }
        catch (Exception)
        {
            // throw original error
        }
    }

    throw;
}

class DesomniaServiceBuilder(bool service) : MadWizard.Desomnia.ApplicationBuilder
{
    internal static string ProgramDir => new FileInfo(Assembly.GetExecutingAssembly().Location).Directory!.FullName;
    internal static string ProgramDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Desomnia");

    protected override string   DefaultLogPath      => service ? Path.Combine(ProgramDataDir, "logs") : base.DefaultLogPath;
    protected override string[] DefaultPluginsPaths => service ? [Path.Combine(ProgramDataDir, "plugins"), Path.Combine(ProgramDir, "plugins"), ] : base.DefaultPluginsPaths;
}