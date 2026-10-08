using Autofac;
using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using MadWizard.Desomnia.Service.Duo.Session;
using MadWizard.Desomnia.Service.Duo.Session.Strategy;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class SessionStartupTests
{
    [Fact]
    public async Task Registry_waits_for_session_monitor_startup_and_adapter_configures_the_existing_watch()
    {
        using var instance = Instance();
        var session = SessionFor(instance);
        var sessions = new FakeSessionManager(session);
        var config = new SessionMonitorConfig();
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        builder.RegisterInstance(NullLogger.Instance).As<ILogger>();
        builder.RegisterType<ProcessMetricsWatch>();
        using var scope = builder.Build();
        using var monitor = new SessionMonitor(config, sessions)
        {
            Scope = scope, Logger = NullLogger<SessionMonitor>.Instance
        };
        using var duo = Monitor(new FakeDuoService(), _ => throw new InvalidOperationException("Not starting the service."));
        using var adapter = new SessionWatchAdapter(config)
        {
            SessionMonitor = monitor, DuoSessionMonitor = duo, Strategy = new RemoteClientStrategy()
        };
        adapter.Attach();
        duo.StartTracking(instance);
        using var registry = new TestDuoRegistry();
        using var key = registry.Key.CreateSubKey(@"Instances\Player");
        key.SetValue("SessionId", (int)session.Id);
        var watcher = new RegistryWatcher { SessionMonitor = monitor, Logger = NullLogger<RegistryWatcher>.Instance };
        await using var run = new WatchRun(watcher, instance);
        Assert.Null(instance.Session);
        Assert.Equal(0, session.LogoffSubscribers);

        await ((IHostedService)monitor).StartAsync(default);
        await session.LogoffSubscribed.Task.WaitAsync(TestTimeout);
        var watch = Assert.Single(monitor);
        Assert.Same(session, instance.Session);
        Assert.Contains(watch, instance);
        Assert.True(watch.Watch.IsYield);

        duo.StopTracking(instance);
        Assert.Empty(instance);
        await run.DisposeAsync();
        Assert.Equal(0, session.LogoffSubscribers);
        await ((IHostedService)monitor).StopAsync(default);
    }
}
