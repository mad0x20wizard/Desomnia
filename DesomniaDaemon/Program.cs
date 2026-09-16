using MadWizard.Desomnia;
using MadWizard.Desomnia.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;

if (!Environment.IsPrivilegedProcess)
    throw new NotSupportedException("The application must be run with root privileges.");

using var mutex = new SystemMutex("MadWizard.Desomnia", true);

DesomniaDaemonBuilder builder;
{
    if (SystemdHelpers.IsSystemdService())
    {
        builder = new DesomniaSystemDaemonBuilder(args);
    }
    else
    {
        builder = new DesomniaDaemonBuilder(args);
    }

    builder.RegisterModule<MadWizard.Desomnia.CoreModule>();

    builder.RegisterModule<MadWizard.Desomnia.Daemon.PlatformModule>();

    builder.RegisterModule<MadWizard.Desomnia.Network.Module>();
    builder.RegisterModule<MadWizard.Desomnia.PowerRequest.Module>();
    builder.RegisterModule<MadWizard.Desomnia.Processes.Module>();

#if DESOMNIA_AOT
    // Plugins can't be loaded dynamically under AOT; statically include the ones we ship.
    builder.RegisterModule<MadWizard.Desomnia.Network.FirewallKnockOperator.PluginModule>();
    builder.RegisterModule<MadWizard.Desomnia.Network.FRITZ.PluginModule>();
#else
    builder.RegisterPluginModules();
#endif

    using (var host = builder.Build())
    {
        host.Run();
    }
}

return Environment.ExitCode;

class DesomniaDaemonBuilder(string[] args) : SystemApplicationBuilder(args)
{
    // Filesystem Hierarchy Standard
    const string FHS_CONFIG_PATH        = "/etc/desomnia";
    const string FHS_LOG_PATH           = "/var/log/desomnia";

    const string FHS_CORE_PLUGINS_PATH  = "/usr/lib/desomnia/plugins";
    const string FHS_USER_PLUGINS_PATH  = "/var/lib/desomnia/plugins";

    private bool useFHS = false;

    protected override string[] DefaultConfigPaths  => [.. base.DefaultConfigPaths, FHS_CONFIG_PATH];

    protected override string LookupConfigPath()
    {
        var path = base.LookupConfigPath();

        useFHS = path.StartsWith(FHS_CONFIG_PATH);

        return path;
    }

    protected override string[] DefaultPluginsPaths => useFHS ? [FHS_CORE_PLUGINS_PATH, FHS_USER_PLUGINS_PATH] : base.DefaultPluginsPaths;
    protected override string   DefaultLogPath      => useFHS ? FHS_LOG_PATH : base.DefaultLogPath;
}

class DesomniaSystemDaemonBuilder(string[] args) : DesomniaDaemonBuilder(args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSystemd();
    }
}