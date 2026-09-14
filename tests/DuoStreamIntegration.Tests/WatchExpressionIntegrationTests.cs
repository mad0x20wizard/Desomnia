using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Service.Duo.Sunshine.Watch;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections;
using System.Diagnostics;
using Xunit;

namespace DuoStreamIntegration.Tests;

public sealed class WatchExpressionIntegrationTests
{
    [Fact]
    public void Stop_notification_waits_until_the_associated_session_watch_is_detached()
    {
        using var instance = Instance("Input");
        using var watch = Session(new TestSession());
        var watcher = DuoTestSupport.Watcher(new ControlledManager());
        var changes = new List<bool>();
        ISession? sessionAtNotification = null;
        var stopped = 0;

        instance.IsRunning = true;
        instance.StartTracking(watch);
        watcher.StatusChanged += (_, args) =>
        {
            changes.Add(args.Status);
            sessionAtNotification = args.Instance.Session;
        };
        instance.Stopped += _ =>
        {
            stopped++;
            return Task.CompletedTask;
        };

        watcher.Publish(instance, false);

        Assert.False(instance.IsRunning);
        Assert.Empty(changes);
        Assert.Equal(0, stopped);

        instance.StopTracking(watch);

        Assert.Equal(new[] { false }, changes);
        Assert.Null(sessionAtNotification);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void Restart_cancels_a_stop_notification_waiting_for_session_detachment()
    {
        using var instance = Instance("Input");
        using var watch = Session(new TestSession());
        var watcher = DuoTestSupport.Watcher(new ControlledManager());
        var changes = new List<bool>();
        var stopped = 0;

        instance.IsRunning = true;
        instance.StartTracking(watch);
        watcher.StatusChanged += (_, args) => changes.Add(args.Status);
        instance.Stopped += _ =>
        {
            stopped++;
            return Task.CompletedTask;
        };

        watcher.Publish(instance, false);
        watcher.Publish(instance, true);
        instance.StopTracking(watch);

        Assert.True(instance.IsRunning);
        Assert.Equal(new[] { true }, changes);
        Assert.Equal(0, stopped);
    }

    [Fact]
    public void Stop_notification_waits_for_all_attached_session_watches()
    {
        using var instance = Instance("Input");
        using var first = Session(new TestSession());
        using var second = Session(new TestSession());
        var watcher = DuoTestSupport.Watcher(new ControlledManager());
        var changes = new List<bool>();

        instance.IsRunning = true;
        instance.StartTracking(first);
        instance.StartTracking(second);
        watcher.StatusChanged += (_, args) => changes.Add(args.Status);

        watcher.Publish(instance, false);
        instance.StopTracking(first);

        Assert.Empty(changes);

        instance.StopTracking(second);

        Assert.Equal(new[] { false }, changes);
    }

    [Fact]
    public async Task Logout_stops_the_associated_instance()
    {
        var info = new DuoInstanceWatchInfo
        {
            Name = "Player",
            Watch = new WatchExpression("Input"),
            WatchStreamTraffic = false,
            OnLogout = new ScheduledActionInfo("stop", null, TimeSpan.Zero)
        };
        using var instance = new DuoInstance("Player", DuoTestSupport.Settings(), info);
        using var watch = Session(new TestSession());
        using var sessions = new SessionMonitor(new SessionMonitorConfig(), null!)
        {
            Logger = NullLogger<SessionMonitor>.Instance,
            Scope = null! // Only managed tracking events are exercised here.
        };
        var manager = new ControlledManager { OnQuery = (_, _) => Task.FromResult(true) };
        var watcher = DuoTestSupport.Watcher(manager);
        manager.OnChange = (target, running, _) =>
        {
            watcher.Publish(target, running);
            return Task.CompletedTask;
        };
        var context = DuoTestSupport.Context(manager, watcher, instance);
        using var duo = DuoTestSupport.Monitor(new FakeDuoService(), _ => new Owned<DuoServiceContext>(context, context));
        using var adapter = new SessionWatchAdapter(new SessionMonitorConfig())
        {
            DuoSessionMonitor = duo,
            SessionMonitor = sessions
        };
        adapter.Attach();
        duo.Startup();
        sessions.StartTracking(watch);

        Assert.Contains(watch, instance);
        Assert.True(instance.IsRunning);

        await ((IEventSystem)watch)[nameof(SessionWatch.Logout)]
            .TriggerEventAsync().WaitAsync(DuoTestSupport.TestTimeout);

        Assert.Equal(1, manager.Stops);
        Assert.False(instance.IsRunning);
    }

    [Fact]
    public void Attaching_instance_tolerates_a_session_added_while_existing_sessions_are_claimed()
    {
        using var instance = Instance("Input");
        using var first = Session(new TestSession());
        using var second = Session(new TestSession());
        using var sessions = new SessionMonitor(new SessionMonitorConfig(), null!)
        {
            Logger = NullLogger<SessionMonitor>.Instance,
            Scope = null! // Only managed tracking events are exercised here.
        };
        using var duo = DuoTestSupport.Monitor(new FakeDuoService(), _ => throw new InvalidOperationException());
        using var adapter = new SessionWatchAdapter(new SessionMonitorConfig())
        {
            DuoSessionMonitor = duo,
            SessionMonitor = sessions
        };
        adapter.Attach();
        sessions.StartTracking(first);
        // Force a session arrival between iterations of the adapter's existing-session
        // scan. StartTracking deduplicates the second arrival during nested callbacks.
        instance.TrackingStarted += (_, _) => sessions.StartTracking(second);

        var error = Record.Exception(() => duo.StartTracking(instance));

        Assert.Null(error);
        Assert.Contains(first, instance);
        Assert.Contains(second, instance);
    }

    [Fact]
    public void StreamTrafficIsSuppliedOnlyByAChildNetworkServiceWatch()
    {
        var withoutWatch = Instance("StreamTraffic");
        AttachYieldingSession(withoutWatch);

        Assert.Throws<InvalidOperationException>(() => withoutWatch.Inspect(TimeSpan.FromSeconds(1)).ToArray());

        var instance = Instance("StreamTraffic");
        AttachYieldingSession(instance);
        var network = new TestNetworkServiceWatch();
        instance.StartTracking(network);

        Assert.Empty(instance.Inspect(TimeSpan.FromSeconds(1)));

        network.HasTraffic = true;

        Assert.Single(instance.Inspect(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void YieldingSessionIsSynchronizedFromTheFinalDuoResult()
    {
        var session = new TestSession { CurrentIdleTime = TimeSpan.Zero };
        var watch = Session(session);
        watch.ApplyConfiguration(new SessionMonitorConfig(), new DuoInstanceWatchInfo
        {
            Name = "Player",
            Watch = WatchExpression.Yield,
            //MaxLastInputTime = TimeSpan.FromMinutes(5),
        });

        var demands = 0;
        var idles = 0;
        watch.Demand += _ => { demands++; return Task.CompletedTask; };
        watch.Idle += _ => { idles++; return Task.CompletedTask; };

        // Yield produces transport usage, but automatic SessionWatch events are suppressed.
        Assert.Single(watch.Inspect(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, demands);
        Assert.Equal(0, idles);

        var instance = Instance("Input");
        instance.StartTracking(watch);

        Assert.Single(instance.Inspect(TimeSpan.FromSeconds(1)));
        Assert.False(watch.IsIdle);
        Assert.Equal(1, demands);

        session.CurrentIdleTime = TimeSpan.FromMinutes(10);

        Assert.Empty(instance.Inspect(TimeSpan.FromSeconds(1)));
        Assert.True(watch.IsIdle);
        Assert.Equal(1, idles);
    }

    [Fact]
    public void DuoEvaluatesProcessSessionAndStreamMetricsTogether()
    {
        var process = new TestProcess { Cpu = TimeSpan.Zero };
        var session = new TestSession(process) { CurrentIdleTime = TimeSpan.Zero };
        var watch = Session(session);
        var sessionConfig = new SessionMonitorConfig();

        watch.ApplyConfiguration(sessionConfig, new SessionWatchInfo
        {
            Watch = new WatchExpression("CPU and Input"),
            MinCPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)),
            //MaxLastInputTime = TimeSpan.FromMinutes(5),
        });

        var duoConfig = new DuoInstanceWatchInfo
        {
            Name = "Player",
            Watch = new WatchExpression("and StreamTraffic"),
            WatchStreamTraffic = false,
        };
        var instance = new DuoInstance("Player", DuoTestSupport.Settings(), duoConfig);
        instance.Watch = watch.Watch << instance.Info.Watch;
        watch.ApplyConfiguration(sessionConfig, duoConfig with { Watch = WatchExpression.Yield, OnIdle = null });
        instance.StartTracking(watch);

        var network = new TestNetworkServiceWatch { HasTraffic = true };
        instance.StartTracking(network);

        Assert.Empty(instance.Inspect(TimeSpan.FromSeconds(1)));

        process.Cpu = TimeSpan.FromMilliseconds(100);

        var usage = Assert.IsType<DuoSessionUsage>(Assert.Single(instance.Inspect(TimeSpan.FromSeconds(1))));
        Assert.True(usage.Metrics["CPU"]);
        Assert.True(usage.Metrics["Input"]);
        Assert.True(usage.Metrics["StreamTraffic"]);

        network.HasTraffic = false;
        process.Cpu = TimeSpan.FromMilliseconds(200);

        Assert.Empty(instance.Inspect(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SessionZeroThresholdReplacesInheritedThresholdAndStillSuppliesTheMetric()
    {
        var process = new TestProcess { Cpu = TimeSpan.Zero };
        var session = new TestSession(process) { CurrentIdleTime = TimeSpan.Zero };
        var watch = Session(session);
        var config = new SessionMonitorConfig();

        watch.ApplyConfiguration(config, new SessionWatchInfo
        {
            Watch = new WatchExpression("CPU"),
            MinCPU = new ProcessingThreshold(TimeSpan.FromMinutes(1)),
        });
        watch.ApplyConfiguration(config, new SessionWatchInfo
        {
            MinCPU = new ProcessingThreshold(TimeSpan.Zero),
        });

        var usage = Assert.IsType<SessionUsage>(Assert.Single(watch.Inspect(TimeSpan.FromSeconds(1))));

        Assert.True(usage.Metrics["CPU"]);
        Assert.Equal(TimeSpan.Zero, usage.Metrics?.Processor?.Time);
    }

    private static DuoInstance Instance(string expression)
    {
        var info = new DuoInstanceWatchInfo
        {
            Name = "Player",
            Watch = new WatchExpression(expression),
            WatchStreamTraffic = false,
        };
        return new DuoInstance("Player", DuoTestSupport.Settings(), info) { Watch = info.Watch };
    }

    private static void AttachYieldingSession(DuoInstance instance)
    {
        var session = new TestSession { CurrentIdleTime = TimeSpan.Zero };
        var watch = Session(session);

        watch.ApplyConfiguration(new SessionMonitorConfig(), new SessionWatchInfo
        {
            Watch = WatchExpression.Yield,
        });
        instance.StartTracking(watch);
    }

    private static SessionWatch Session(TestSession session) => new(session)
    {
        Session = session,
        CreateAnyProcessWatch = metrics => new AnySessionProcessWatch
        {
            Manager = session,
            MetricsWatch = new ProcessMetricsWatch(metrics),
        },
        CreateProcessWatch = _ => throw new InvalidOperationException("No process resources configured."),
    };

    private sealed class TestNetworkServiceWatch() : NetworkServiceWatch(new SunshineService("Player", 47989))
    {
        public bool HasTraffic { get; set; }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (HasTraffic)
                yield return new NetworkServiceUsage(Service, 1);
        }
    }

    private sealed class TestSession(params IProcess[] processes) : ISession
    {
        private readonly IReadOnlyList<IProcess> _processes = processes;

        public TimeSpan? CurrentIdleTime { get; set; }

        public uint Id => 1;
        public string UserName => "player";
        public string? ClientName => "Player";
        public bool IsConnected => true;
        public bool IsConsoleConnected => false;
        public bool IsRemoteConnected => true;
        public bool IsAdministrator => false;
        public bool IsUser => true;
        public bool? IsLocked => false;
        public TimeSpan? IdleTime => CurrentIdleTime;
        public DateTime? LastInputTime => CurrentIdleTime is TimeSpan idle ? DateTime.Now - idle : null;
        public IProcess this[int pid] => _processes.Single(process => process.Id == pid);

        public Task Disconnect() => Task.CompletedTask;
        public Task Logoff() => Task.CompletedTask;
        public Task Lock() => Task.CompletedTask;
        public IProcess LaunchProcess(ProcessStartInfo info) => throw new NotSupportedException();
        public IEnumerator<IProcess> GetEnumerator() => _processes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public event EventHandler Locked { add { } remove { } }
        public event EventHandler Unlocked { add { } remove { } }
        public event EventHandler Connected { add { } remove { } }
        public event EventHandler Disconnected { add { } remove { } }
        public event EventHandler<IProcess> ProcessStarted { add { } remove { } }
        public event EventHandler<IProcess> ProcessStopped { add { } remove { } }
    }

    private sealed class TestProcess : IProcess
    {
        public TimeSpan? Cpu { get; set; }

        public int Id => 1;
        public int SessionId => 1;
        public string Name => "game";
        public string? ImagePath => null;
        public TimeSpan? ProcessorTime => Cpu;
        public IProcess? Parent => null;
        public bool HasStopped => false;
        public Process Native => throw new NotSupportedException();

        public Task Stop(TimeSpan timeout = default) => Task.CompletedTask;
        public event EventHandler Stopped { add { } remove { } }
        public void Dispose() { }
    }
}
