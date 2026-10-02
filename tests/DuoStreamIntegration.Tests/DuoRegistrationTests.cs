using Autofac;
using Autofac.Builder;
using Autofac.Core;
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
    [InlineData(WatchMode.Polling, null, typeof(PollingWatcher))]
    [InlineData(WatchMode.Registry, null, typeof(RegistryWatcher))]
    [InlineData(WatchMode.Auto, "1.5.6", typeof(RegistryWatcher), typeof(PollingWatcher))]
    [InlineData(WatchMode.Auto, "1.6.1", typeof(RegistryWatcher), typeof(PreciseEventWatcher), typeof(PollingWatcher))]
    [InlineData(WatchMode.Capture, "1.6.1", typeof(RegistryWatcher), typeof(PreciseEventWatcher), typeof(PollingWatcher))]
    [InlineData(WatchMode.EventLog, "1.5.7", typeof(EventWatcher))]
    [InlineData(WatchMode.EventLog, "1.6.0", typeof(EventWatcher))]
    [InlineData(WatchMode.EventLog, "1.6.1", typeof(PreciseEventWatcher))]
    [InlineData(WatchMode.EventLog | WatchMode.Polling, "1.5.6", typeof(PollingWatcher))]
    [InlineData(WatchMode.EventLog | WatchMode.Polling, "1.6.1", typeof(PreciseEventWatcher), typeof(PollingWatcher))]
    [InlineData(WatchMode.Registry | WatchMode.EventLog | WatchMode.Polling, "1.6.1", typeof(RegistryWatcher), typeof(PreciseEventWatcher), typeof(PollingWatcher))]
    [InlineData(WatchMode.EventLog, "1.5.6")]
    public void Module_resolves_a_composite_with_all_compatible_watchers(WatchMode mode, string? version, params Type[] watcherTypes)
    {
        using var serviceVersion = new TestServiceVersion(version);
        var config = new DuoConfig
        {
            SessionMonitor = new MadWizard.Desomnia.Session.Configuration.SessionMonitorConfig(),
            DuoSessionMonitor = new DuoSessionMonitorConfig
            {
                ServiceName = "TestDuo", WatchMode = mode, PollInterval = TimeSpan.FromSeconds(4)
            }
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
        Assert.IsType<CompositeWatcher>(owned.Value.Watcher);
        using var scope = container.BeginLifetimeScope(new TypedService(typeof(DuoServiceContext)));
        var watchers = scope.Resolve<IEnumerable<IDuoWatcher>>().ToArray();
        Assert.Equal(new[] { typeof(SessionWatcher) }.Concat(watcherTypes), watchers.Select(w => w.GetType()));
        if (watchers.OfType<PollingWatcher>().SingleOrDefault() is PollingWatcher polling)
            Assert.Equal(config.DuoSessionMonitor.PollInterval, polling.PollInterval);
        Assert.Equal("Player", Assert.Single(owned.Value.Instances).Name);

        Assert.NotSame(owned.Value.Manager, replacement.Value.Manager);
        Assert.NotSame(owned.Value.Watcher, replacement.Value.Watcher);
    }

    [Fact]
    public void Duo_monitor_requires_a_session_monitor_configuration()
    {
        var config = new DuoConfig
        {
            DuoSessionMonitor = new DuoSessionMonitorConfig { ServiceName = "TestDuo" }
        };

        var error = Assert.Throws<FormatException>(() => new TestModule().Configure(new ContainerBuilder(), config));
        Assert.Contains("<SessionMonitor>", error.Message);
    }

    private sealed class TestModule : PluginModule
    {
        public void Configure(ContainerBuilder builder, DuoConfig config) => base.Load(builder, config);
    }
}
