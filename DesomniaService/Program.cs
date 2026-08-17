using Autofac;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Service.Windows;
using System.Diagnostics;
using System.Reflection;

//MadWizard.Desomnia.Test.Debugger.UntilAttached().Wait();

if (!Environment.IsPrivilegedProcess)
    throw new NotSupportedException("The application must be run with elevated privileges.");

using var mutex = new SystemMutex("MadWizard.Desomnia", true);

DesomniaWindowsBuilder builder;

if (Environment.IsWindowsService)
{
    builder = new DesomniaWindowsServiceBuilder();

    builder.RegisterModule<WindowsServiceModule>();
}
else
{
    builder = new DesomniaWindowsBuilder(args);
}

try
{
    builder.RegisterModule<MadWizard.Desomnia.CoreModule>();

    builder.RegisterModule<MadWizard.Desomnia.Service.PlatformModule>();

    builder.RegisterModule<MadWizard.Desomnia.Display.Module>();
    builder.RegisterModule<MadWizard.Desomnia.Network.Module>();
    builder.RegisterModule<MadWizard.Desomnia.NetworkSession.Module>();
    builder.RegisterModule<MadWizard.Desomnia.PowerRequest.Module>();
    builder.RegisterModule<MadWizard.Desomnia.Processes.Module>();
    builder.RegisterModule<MadWizard.Desomnia.Session.Module>();

    builder.RegisterPluginModules();

    using (var host = builder.Build())
    {
        host.Run();
    }

    return Environment.ExitCode;
}
catch (Exception ex)
{
    if (builder is DesomniaWindowsServiceBuilder srv)
    {
        try
        {
            srv.WriteErrorToEventLog(ex);

            return 1;
        }
        catch (Exception)
        {
            // throw original error
        }
    }

    throw;
}

class DesomniaWindowsBuilder(params string[] args) : MadWizard.Desomnia.ApplicationBuilder(args)
{

}

class DesomniaWindowsServiceBuilder : DesomniaWindowsBuilder
{
    const string EVENT_LOG_NAME = "Application";
    const string EVENT_LOG_SOURCE = "Desomnia";

    static string ProgramDir => new FileInfo(Assembly.GetExecutingAssembly().Location).Directory!.FullName;
    static string ProgramDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Desomnia");

    protected override string[] DefaultConfigPaths  => [Path.Combine(ProgramDataDir, "config")];
    protected override string[] DefaultPluginsPaths => [Path.Combine(ProgramDataDir, "plugins"), Path.Combine(ProgramDir, "plugins")];
    protected override string   DefaultLogPath      =>  Path.Combine(ProgramDataDir, "logs");

    internal DesomniaWindowsServiceBuilder() : base()
    {
        Directory.SetCurrentDirectory(ProgramDataDir);

        _source.ReloadOnChange = true;

        CreateEventLog();
    }

    private static void CreateEventLog()
    {
        if (!EventLog.SourceExists(EVENT_LOG_SOURCE))
        {
            EventLog.CreateEventSource(EVENT_LOG_SOURCE, EVENT_LOG_NAME);
        }
    }

    internal void WriteErrorToEventLog(Exception ex)
    {
        EventLog.WriteEntry(EVENT_LOG_SOURCE, $"{ex}", EventLogEntryType.Error);
    }
}
