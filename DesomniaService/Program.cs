using Autofac;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Logging;
using MadWizard.Desomnia.Service.Windows;
using Microsoft.Extensions.Hosting;
using NLog;
using System.Diagnostics;
using System.Reflection;

//await MadWizard.Desomnia.Test.Debugger.UntilAttached();

LogManager.Setup().SetupExtensions(ext => ext.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration")); // FIXME

if (Process.GetCurrentProcess().IsWindowsService() is bool isRunningAsService && isRunningAsService)
{
    var applicationDir = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory!;

    Directory.SetCurrentDirectory(applicationDir.FullName);
}

string logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");

const string EVENT_LOG_NAME = "Application";
const string EVENT_LOG_SOURCE = "Desomnia";

string configPath = new ConfigDetector().Lookup();

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
            var builder = new DesomniaServiceBuilder();

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

            builder.Build().RunAsync(watcher.Token).Wait();
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
            try
            {
                File.WriteAllText(Path.Combine(logsPath, "error.log"), $"{ex}");

                return 1;
            }
            catch
            {
                // throw original error
            }
        }
    }

    throw;
}

class DesomniaServiceBuilder() : MadWizard.Desomnia.ApplicationBuilder
{

}