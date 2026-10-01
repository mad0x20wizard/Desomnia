using Autofac;
using Autofac.Builder;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class DuoRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Module_resolves_an_owned_context_using_the_current_interfaces(bool polling)
    {
        var config = new DuoConfig
        {
            SessionMonitor = new MadWizard.Desomnia.Session.Configuration.SessionMonitorConfig(),
            DuoSessionMonitor = new DuoSessionMonitorConfig { ServiceName = "TestDuo", UsePolling = polling }
        };
        config.NetworkMonitor.Add(new NetworkMonitorConfig()); // No firewall listener registration.
        var builder = new ContainerBuilder();
        var sessions = new FakeSessionManager();
        builder.RegisterInstance(sessions).As<MadWizard.Desomnia.Session.Manager.ISessionManager>();
        builder.RegisterInstance(new MadWizard.Desomnia.Session.SessionMonitor(config.SessionMonitor, sessions)
        {
            Scope = null!, Logger = NullLogger<MadWizard.Desomnia.Session.SessionMonitor>.Instance
        });
        new TestModule().Configure(builder, config);
        builder.RegisterInstance(NullLogger.Instance).As<ILogger>();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        using var container = builder.Build(ContainerBuildOptions.IgnoreStartableComponents);
        var create = container.Resolve<Func<DuoSettings, Owned<DuoServiceContext>>>();

        using var owned = create(new DuoSettings { Port = 38299, Instances = [Settings()] });
        using var replacement = create(new DuoSettings { Port = 38300, Instances = [Settings()] });

        Assert.IsType<DuoWebAPIManager>(owned.Value.Manager);
        if (polling)
            Assert.IsType<PollingWatcher>(owned.Value.Watcher);
        else
            Assert.IsType<RegistryWatcher>(owned.Value.Watcher);
        Assert.Equal("Player", Assert.Single(owned.Value.Instances).Name);
        Assert.Equal(38299u, owned.Value.Settings.Port);
        Assert.NotSame(owned.Value.Manager, replacement.Value.Manager);
        Assert.NotSame(owned.Value.Watcher, replacement.Value.Watcher);
    }

    private sealed class TestModule : PluginModule
    {
        public void Configure(ContainerBuilder builder, DuoConfig config) => base.Load(builder, config);
    }
}
