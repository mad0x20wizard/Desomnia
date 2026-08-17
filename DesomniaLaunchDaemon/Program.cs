using MadWizard.Desomnia;

if (!Environment.IsPrivilegedProcess)
    throw new NotSupportedException("The application must be run with root privileges.");

using var mutex = new SystemMutex("MadWizard.Desomnia", true);

var builder = new DesomniaLaunchDaemonBuilder(args);
{
    builder.RegisterModule<MadWizard.Desomnia.CoreModule>();

    builder.RegisterModule<MadWizard.Desomnia.LaunchDaemon.PlatformModule>();

    builder.RegisterModule<MadWizard.Desomnia.Display.Module>();
    builder.RegisterModule<MadWizard.Desomnia.Network.Module>();
    builder.RegisterModule<MadWizard.Desomnia.PowerRequest.Module>();
    builder.RegisterModule<MadWizard.Desomnia.Processes.Module>();

#if DESOMNIA_AOT
    // Plugins can't be loaded dynamically under AOT; statically include the ones we ship.
    builder.RegisterModule<MadWizard.Desomnia.Network.FirewallKnockOperator.PluginModule>();
    builder.RegisterModule<MadWizard.Desomnia.Network.FRITZ.PluginModule>();
#else
    // TEST
    // builder.RegisterModule<MadWizard.Desomnia.Network.FRITZ.PluginModule>();

    builder.RegisterPluginModules();
#endif

    using (var host = builder.Build())
    {
        host.Run();
    }
}

return Environment.ExitCode;

class DesomniaLaunchDaemonBuilder(string[] args) : MadWizard.Desomnia.ApplicationBuilder(args)
{
    /// On macOS the StandardOut is written directly to file, so we have to include the timestamp explicitly.
    protected override string DefaultLogConsoleLayout => "${longdate} " + base.DefaultLogConsoleLayout;
}
