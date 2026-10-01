using Autofac;
using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Service.Duo;
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
    public async Task Context_waits_for_session_monitor_startup_before_associating_existing_sessions()
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
        var manager = new ControlledManager { OnQuery = (_, _) => Task.FromResult(true) };
        var watcher = Watcher(manager);
        using var context = new DuoServiceContext
        {
            Settings = new() { Port = 38299, Instances = [Settings()] },
            Manager = manager, Watcher = watcher, Instances = [instance],
            SessionMonitor = monitor, SessionMonitorConfig = config
        };

        var starting = context.StartWatching(TestTimeout);
        Assert.False(starting.IsCompleted);
        Assert.False(watcher.Started);
        Assert.Equal(0, manager.Queries);

        await ((IHostedService)monitor).StartAsync(default);
        await starting.WaitAsync(TestTimeout);
        var watch = Assert.Single(monitor);
        Assert.Same(session, instance.Session);
        Assert.Contains(watch, instance);
        Assert.True(watch.Watch.IsYield);

        ((IDisposable)context).Dispose();
        Assert.Empty(instance);
        Assert.True(watcher.Stopped);
        await ((IHostedService)monitor).StopAsync(default);
    }
}
