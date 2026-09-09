using MadWizard.Desomnia;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using System.Collections;
using System.Diagnostics;
using Xunit;

namespace DuoStreamIntegration.Tests;

public sealed class WatchExpressionIntegrationTests
{
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
        watch.ApplyConfiguration(new SessionMonitorConfig(), new DuoInstanceInfo
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

        var duoConfig = new DuoInstanceInfo
        {
            Name = "Player",
            Watch = new WatchExpression("and StreamTraffic"),
            WatchStreamTraffic = false,
        };
        var instance = new DuoInstance(duoConfig, DuoManagerTests.CreateSettings("Player", userName: "player"));
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

    private static DuoInstance Instance(string expression) => new(
        new DuoInstanceInfo
        {
            Name = "Player",
            Watch = new WatchExpression(expression),
            WatchStreamTraffic = false,
        },
        DuoManagerTests.CreateSettings("Player", userName: "player"));

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
