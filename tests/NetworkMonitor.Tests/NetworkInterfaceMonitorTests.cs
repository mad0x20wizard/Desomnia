using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TestManager = MadWizard.Desomnia.Network.Tests.NetworkInterfaceManagerTests.TestManager;

namespace MadWizard.Desomnia.Network.Tests;

public class NetworkInterfaceMonitorTests
{
    private static NetworkInterfaceMonitor Create(INetworkInterfaceManager manager, params NetworkInterfaceWatchInfo[] rules) => new(rules)
    {
        Manager = manager, CreateMatcher = () => new InterfaceMatcher(), Logger = NullLogger<NetworkInterfaceMonitor>.Instance
    };

    [Fact]
    public async Task StrictWatchEnforcesEachExternalChangeAndRestoresLatestDesire()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0", true);
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true });
        await Reconcile(monitor);
        adapter.Disabled = false;
        manager.Pump();
        Assert.Null(manager.Single().ShouldBeDisabled);
        await Reconcile(monitor);
        Assert.True(adapter.Disabled);
        manager.Single().ShouldBeDisabled = null;
        Assert.False(adapter.Disabled);
    }

    [Fact]
    public async Task AllowedChangesRemainUntilEveryEnvironmentRebuildReappliesConfiguration()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0", true);
        var rule = new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true, AllowToChange = NetworkInterfaceState.Disabled };
        await using (var monitor = Create(manager, rule))
        {
            await Reconcile(monitor);
            adapter.Disabled = false;
            await Reconcile(monitor);
            await Reconcile(monitor);
            Assert.False(adapter.Disabled);
            Assert.Null(manager.Single().ShouldBeDisabled);
        }
        await using (var replacement = Create(manager, rule))
        {
            await Reconcile(replacement);
            Assert.True(adapter.Disabled);
        }
        manager.Dispose();
        Assert.False(adapter.Disabled);
    }

    [Fact]
    public async Task LaterMatchingSelectorsOverwriteOnlyExplicitProperties()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0", true);
        await using var monitor = Create(manager,
            new NetworkInterfaceWatchInfo { Name = "eth.*", Disabled = true, AllowToChange = NetworkInterfaceState.Disabled },
            new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = false },
            new NetworkInterfaceWatchInfo { Name = ".*" });
        await Reconcile(monitor);
        Assert.False(adapter.Disabled);
        adapter.Disabled = true;
        await Reconcile(monitor);
        Assert.True(adapter.Disabled); // inherited allowToChange still applies
        Assert.Equal(1, monitor.WatchCount);
    }

    [Fact]
    public async Task ExplicitNoneOverridesEarlierAllowedChanges()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        await using var monitor = Create(manager,
            new NetworkInterfaceWatchInfo { Name = ".*", Disabled = true, AllowToChange = NetworkInterfaceState.Disabled },
            new NetworkInterfaceWatchInfo { Name = "eth0", AllowToChange = NetworkInterfaceState.None });
        await Reconcile(monitor);
        adapter.Disabled = false;
        await Reconcile(monitor);
        Assert.True(adapter.Disabled);
    }

    [Fact]
    public async Task EmptyEnvironmentReleasesPreviousOverrideAndStillWatchesInterfaces()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        await using (var original = Create(manager, new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true })) await Reconcile(original);
        Assert.True(adapter.Disabled);
        await using var empty = Create(manager);
        await Reconcile(empty);
        Assert.False(adapter.Disabled);
        Assert.Null(manager.Single().ShouldBeDisabled);
        Assert.Equal(1, empty.WatchCount);
    }

    [Fact]
    public async Task RapidPhysicalReattachmentCreatesAFreshWatchDespiteStableHandle()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        var nic = manager.Single();
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true, AllowToChange = NetworkInterfaceState.Disabled });
        await Reconcile(monitor);
        manager.System.Clear();
        manager.Pump();
        var replacement = manager.Add("eth0");
        Assert.Same(nic, manager.Single());
        await Reconcile(monitor);
        Assert.True(replacement.Disabled);
        Assert.Equal(1, monitor.WatchCount);
    }

    [Fact]
    public async Task UnmatchedAndAbsentInterfacesCauseNoWrites()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo { Name = "wlan0", Disabled = true });
        await Reconcile(monitor);
        Assert.Empty(manager.Writes);
        manager.System.Clear();
        manager.Pump();
        await Reconcile(monitor);
        Assert.Equal(0, monitor.WatchCount);
    }

    [Fact]
    public async Task FailedInitialWriteIsRetriedEvenWhenExternalChangesAreAllowed()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true, AllowToChange = NetworkInterfaceState.Disabled });
        manager.FailBeforeWrite = true;
        Assert.Empty(await Reconcile(monitor));
        Assert.Empty(await Reconcile(monitor));
        Assert.True(adapter.Disabled);
    }

    [Fact]
    public async Task FailedAdapterDoesNotPreventOtherInterfacesFromBeingReconciled()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        manager.Add("eth1");
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true });
        manager.FailBeforeWrite = true;
        Assert.Equal("eth1", Assert.Single(await Reconcile(monitor)).Name);
    }

    [Fact]
    public async Task UnreadableAdministrativeStateDoesNotPreventInventoryUpdatesForOtherAdapters()
    {
        using var manager = new TestManager();
        var bad = manager.Add("eth0");
        var removed = manager.Add("eth1");
        await using var monitor = Create(manager);
        await Reconcile(monitor);
        manager.FailReadId = bad.Id;
        manager.System.Remove(removed);
        manager.Add("eth2");
        Assert.Equal("eth2", Assert.Single(await Reconcile(monitor)).Name);
        Assert.DoesNotContain(manager, nic => nic.Name == "eth1");
        Assert.Equal(2, monitor.WatchCount);
    }

    [Fact]
    public async Task ObserverAppliesInitialStateBeforeSelectionAndAdmitsAnAllowedExternalEnable()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo
        {
            Name = "eth0", Disabled = true, AllowToChange = NetworkInterfaceState.Disabled
        });
        int startups = 0;
        var observer = new DynamicNetworkObserver()
        {
            Logger = NullLogger<DynamicNetworkObserver>.Instance, Power = new FakePowerManager(),
            Selector = new NetworkConfigSelector(() => new InterfaceMatcher(), [new NetworkMonitorConfig { Interface = "eth0" }]),
            Monitor = monitor,
            CreateContext = (_, _) => { startups++; throw new InvalidOperationException("Stop before native capture."); }
        };
        await ((IHostedService)observer).StartAsync(CancellationToken.None);
        Assert.True(adapter.Disabled);
        Assert.Equal(0, startups);
        adapter.Disabled = false;
        await Reconcile(monitor);
        Assert.False(adapter.Disabled);
        Assert.Null(manager.Single().ShouldBeDisabled);
        Assert.Equal(1, startups);
        await ((IHostedService)observer).StopAsync(CancellationToken.None);
    }
    [Fact]
    public async Task EventsDuringAwaitedSubscriberCoalesceIntoOneFurtherRound()
    {
        var manager = new FakeNetworkInterfaceManager(new FakeNetworkInterface("eth0"));
        await using var monitor = Create(manager);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int rounds = 0;
        monitor.Changed += async (_, _) =>
        {
            if (Interlocked.Increment(ref rounds) == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            else second.TrySetResult();
        };
        var start = monitor.StartAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var published = monitor.GetAvailableInterfaces();
            for (int i = 0; i < 100; i++) manager.RaiseChanged();
            Assert.Equal(1, rounds);
            Assert.False(start.IsCompleted);
            Assert.Same(published, monitor.GetAvailableInterfaces());
        }
        finally { release.TrySetResult(); }
        await start;
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.StopAsync();
        Assert.Equal(2, rounds);
        Assert.False(manager.HasChangedSubscribers);
    }

    [Fact]
    public async Task EventsDuringEnforcementAreNotLost()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        await using var monitor = Create(manager, new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true });
        manager.OnWrite = () => { for (int i = 0; i < 10; i++) manager.Pump(); };
        int rounds = 0;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref rounds) == 2) second.TrySetResult();
            return Task.CompletedTask;
        };
        await monitor.StartAsync();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.StopAsync();
        Assert.Equal(2, rounds);
        Assert.Single(manager.Writes);
        Assert.Empty(monitor.GetAvailableInterfaces());
    }

    [Fact]
    public async Task StopDrainsTheSubscriberAndDiscardsPendingEnforcement()
    {
        var manager = new FakeNetworkInterfaceManager(new FakeNetworkInterface("eth0"));
        await using var monitor = Create(manager);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int rounds = 0;
        monitor.Changed += async (_, _) => { rounds++; entered.TrySetResult(); await release.Task; };
        var start = monitor.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        manager.RaiseChanged();
        var stop = monitor.StopAsync();
        try
        {
            Assert.False(stop.IsCompleted);
            Assert.False(manager.HasChangedSubscribers);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(5));
        manager.RaiseChanged();
        Assert.Equal(1, rounds);
    }

    [Fact]
    public async Task SubscriberFailureDoesNotKillFurtherProcessing()
    {
        var manager = new FakeNetworkInterfaceManager(new FakeNetworkInterface("eth0"));
        await using var monitor = Create(manager);
        int rounds = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref rounds) == 1
            ? Task.FromException(new InvalidOperationException("Subscriber failed")) : Task.CompletedTask;
        await monitor.StartAsync();
        await Reconcile(monitor);
        Assert.Equal(2, rounds);
    }

    [Fact]
    public async Task MonitoringExclusionHasNoOsEffectAndOmissionInheritsIt()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        manager.Add("wlan0");
        await using var monitor = Create(manager,
            new NetworkInterfaceWatchInfo { Name = "eth.*", Monitor = false },
            new NetworkInterfaceWatchInfo { Name = "eth0" });
        await monitor.StartAsync();
        Assert.Equal("wlan0", Assert.Single(monitor.GetAvailableInterfaces()).Name);
        Assert.Empty(manager.Writes);
        Assert.Equal(2, monitor.WatchCount);
    }

    [Fact]
    public async Task LaterExplicitMonitoringValueOverridesExclusion()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        await using var monitor = Create(manager,
            new NetworkInterfaceWatchInfo { Name = "eth.*", Monitor = false },
            new NetworkInterfaceWatchInfo { Name = "eth0", Monitor = true });
        await monitor.StartAsync();
        Assert.Single(monitor.GetAvailableInterfaces());
        Assert.Empty(manager.Writes);
    }

    [Fact]
    public async Task ExcludedInterfaceStillEnforcesStateAndRemainsExcludedAfterAllowedEnable()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        await using var monitor = Create(manager,
            new NetworkInterfaceWatchInfo { Name = "eth0", Disabled = true, Monitor = false, AllowToChange = NetworkInterfaceState.Disabled });
        await monitor.StartAsync();
        Assert.True(adapter.Disabled);
        adapter.Disabled = false;
        await Reconcile(monitor);
        Assert.False(adapter.Disabled);
        Assert.Empty(monitor.GetAvailableInterfaces());
        Assert.Single(manager.Writes);
    }
    internal static async Task<IReadOnlyList<INetworkInterface>> Reconcile(NetworkInterfaceMonitor monitor)
    {
        if (monitor.Revision == 0)
            await monitor.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        else
        {
            var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task OnChanged(object? sender, EventArgs args) { notified.TrySetResult(); return Task.CompletedTask; }
            monitor.Changed += OnChanged;
            try
            {
                monitor.RequestReconciliation();
                await notified.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally { monitor.Changed -= OnChanged; }
        }
        return monitor.GetAvailableInterfaces();
    }}
