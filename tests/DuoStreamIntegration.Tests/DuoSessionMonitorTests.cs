using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Service.Duo;
using Microsoft.Extensions.Logging.Abstractions;
using System.ServiceProcess;
using System.Threading.Channels;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class DuoSessionMonitorTests
{
    [Fact]
    public async Task Service_restart_cancels_old_commands_and_uses_a_fresh_context()
    {
        using var oldInstance = Instance();
        using var replacement = Instance();
        var entered = Signal();
        var oldManager = new ControlledManager
        {
            OnChange = async (_, _, token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        var oldWatcher = Watcher(oldManager);
        var oldContext = Context(oldManager, oldWatcher, oldInstance);
        var newManager = new ControlledManager();
        var newWatcher = Watcher(newManager);
        var newContext = Context(newManager, newWatcher, replacement);
        newManager.OnChange = (target, running, _) =>
        {
            newWatcher.Publish(target, running);
            return Task.CompletedTask;
        };
        var contexts = new Queue<DuoServiceContext>([oldContext, newContext]);
        var service = new FakeDuoService();
        using var monitor = Monitor(service, _ =>
        {
            var context = contexts.Dequeue();
            return new Owned<DuoServiceContext>(context, context);
        });
        await TrackInstance(monitor, oldInstance, monitor.Startup);
        var pending = monitor.HandleActionStart(oldInstance);
        await entered.Task.WaitAsync(TestTimeout);
        var queued = monitor.HandleActionStop(oldInstance);

        service.Publish(ServiceControllerStatus.Stopped);
        await TrackInstance(monitor, replacement, () => service.Publish(ServiceControllerStatus.Running));
        await Task.WhenAll(pending, queued).WaitAsync(TestTimeout);

        Assert.True(oldWatcher.Stopped);
        Assert.Same(replacement, Assert.Single(monitor));
        await monitor.HandleActionStart(oldInstance); // A stale action cannot target the new API.
        Assert.Equal(0, newManager.Starts);
        await monitor.HandleActionStart(replacement);
        Assert.NotNull(replacement.Session);
        Assert.Equal(1, oldManager.Starts);
        Assert.Equal(0, oldManager.Stops);
        Assert.Equal(1, newManager.Starts);
    }

    [Fact]
    public async Task Monitor_snapshot_tolerates_a_service_restart_during_iteration()
    {
        using var oldInstance = Instance();
        using var replacement = Instance();
        var manager = new ControlledManager();
        var contexts = new Queue<DuoServiceContext>([
            Context(manager, Watcher(manager), oldInstance),
            Context(manager, Watcher(manager), replacement)
        ]);
        var service = new FakeDuoService();
        using var monitor = Monitor(service, _ =>
        {
            var context = contexts.Dequeue();
            return new Owned<DuoServiceContext>(context, context);
        });
        await TrackInstance(monitor, oldInstance, monitor.Startup);
        using var iterator = ((IEnumerable<DuoInstance>)monitor.TakeSnapshot()).GetEnumerator();
        Assert.True(iterator.MoveNext());

        // The adapter only reads. All mutations go through the monitor's actual
        // service-status handler, between two steps of the reader's enumeration.
        service.Publish(ServiceControllerStatus.Stopped);
        await TrackInstance(monitor, replacement, () => service.Publish(ServiceControllerStatus.Running));

        var error = Record.Exception(() => { while (iterator.MoveNext()) { } });
        Assert.Null(error);
    }

    [Fact]
    public async Task Service_stop_untracks_instances_and_disposes_the_owned_context()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        var context = Context(manager, watcher, instance);
        var service = new FakeDuoService();
        using var monitor = Monitor(service, _ => new Owned<DuoServiceContext>(context, context));
        var removed = false;
        monitor.TrackingStopped += (_, args) => removed = args.Inspectable == instance;
        await TrackInstance(monitor, instance, monitor.Startup);
        Assert.Same(instance, Assert.Single(monitor));

        service.Publish(ServiceControllerStatus.Stopped);

        Assert.True(removed);
        Assert.Empty(monitor);
        Assert.True(watcher.Stopped);
    }

    [Fact]
    public async Task Disposed_monitor_rejects_an_already_queued_running_notification()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        var context = Context(manager, watcher, instance);
        var service = new FakeDuoService();
        var created = 0;
        using var monitor = Monitor(service, _ =>
        {
            created++;
            return new Owned<DuoServiceContext>(context, context);
        });
        await TrackInstance(monitor, instance, monitor.Startup);
        var queued = service.CaptureRunningNotification();

        monitor.Dispose();
        queued();

        Assert.True(watcher.Stopped);
        Assert.Equal(1, created);
        Assert.Empty(monitor);
    }

    [Fact, Trait("Issue", "5")]
    public async Task A_transient_initial_query_failure_recovers_while_service_stays_running()
    {
        var manager = new ControlledManager();
        var queries = 0;
        manager.OnQuery = (_, _) => Interlocked.Increment(ref queries) == 1
            ? Task.FromException<bool>(new HttpRequestException("Endpoint is not ready yet"))
            : Task.FromResult(true);
        var service = new FakeDuoService();
        var instances = new List<DuoInstance>();
        using var monitor = Monitor(service, _ =>
        {
            var instance = Instance();
            instances.Add(instance);
            var context = Context(manager, Watcher(manager), instance);
            return new Owned<DuoServiceContext>(context, context);
        });
        var tracked = Signal();
        monitor.TrackingStarted += (_, _) => tracked.TrySetResult();
        try
        {
            monitor.Startup();
            // No second SCM status notification: the service has remained Running.
            // Production waits ten seconds before retrying; allow time for that retry.
            var recovered = await Task.WhenAny(tracked.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(recovered == tracked.Task, "Monitoring never retried the failed initial query while Duo remained Running (issue 5).");
            Assert.NotNull(Assert.Single(monitor).Session);
        }
        finally
        {
            monitor.Dispose();
            foreach (var instance in instances) instance.Dispose();
        }
    }

    [Fact, Trait("Issue", "5")]
    public async Task Stopping_the_service_during_retry_delay_does_not_raise_an_unhandled_exception()
    {
        var service = new FakeDuoService();
        var attempted = false;
        using var monitor = Monitor(service, _ =>
        {
            attempted = true;
            throw new HttpRequestException("Endpoint is not ready yet");
        });
        var callbacks = new QueuedSynchronizationContext();
        callbacks.Start(() => monitor.Startup());
        Assert.Empty(await callbacks.RunUntilAsync(() => attempted, TestTimeout));

        service.Publish(ServiceControllerStatus.Stopped);

        var errors = await callbacks.RunPostedCallbacksAsync(TestTimeout);
        Assert.Empty(errors);
        Assert.Empty(monitor);
    }

    [Fact, Trait("Issue", "5")]
    public async Task Disposing_the_monitor_prevents_a_pending_retry_from_creating_a_context()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var context = Context(manager, Watcher(manager), instance);
        var created = 0;
        using var monitor = Monitor(new FakeDuoService(), _ =>
        {
            if (++created == 1) throw new HttpRequestException("Endpoint is not ready yet");
            return new Owned<DuoServiceContext>(context, context);
        });
        var callbacks = new QueuedSynchronizationContext();
        callbacks.Start(() => monitor.Startup());
        Assert.Empty(await callbacks.RunUntilAsync(() => created == 1, TestTimeout));

        monitor.Dispose();

        // Resume the pending delay deterministically after disposal. A fixed version
        // may wake immediately through cancellation, or finish the existing delay.
        await callbacks.RunPostedCallbacksAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(1, created);
        Assert.Empty(monitor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_state_is_loaded_and_manual_changes_after_startup_are_observed(bool initiallyRunning)
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = new TestEventWatcher { SessionManager = new FakeSessionManager(), Manager = manager, Logger = NullLogger.Instance };
        manager.OnQuery = (_, _) => Task.FromResult(initiallyRunning);
        var changes = new List<bool>();
        watcher.SessionChanged += (_, args) => changes.Add(args.Session is not null);
        using var context = Context(manager, watcher, instance);
        await context.StartWatching(TestTimeout);
        await watcher.Subscribed.Task.WaitAsync(TestTimeout);
        Assert.Equal(initiallyRunning, (instance.Session is not null));
        Assert.Equal(initiallyRunning ? new[] { true } : [], changes);
        changes.Clear(); // Initial association is published so the adapter can acquire the watch.

        // Changes during the initial query/subscription handover are an accepted
        // limitation. Manual changes after startup must work in both directions.
        var first = initiallyRunning ? DuoEventID.InstanceStopped : DuoEventID.InstanceStarted;
        var second = initiallyRunning ? DuoEventID.InstanceStarted : DuoEventID.InstanceStopped;
        await watcher.SendAsync(first, instance.Name).WaitAsync(TestTimeout);
        Assert.Equal(!initiallyRunning, (instance.Session is not null));
        await watcher.SendAsync(first, instance.Name).WaitAsync(TestTimeout);
        Assert.Single(changes); // Repeated notifications must not repeat the transition.
        await watcher.SendAsync(second, instance.Name).WaitAsync(TestTimeout);
        Assert.Equal(initiallyRunning, (instance.Session is not null));
        Assert.Equal(new[] { !initiallyRunning, initiallyRunning }, changes);
    }

    // async void reports escaped exceptions to its captured synchronization context.
    // Capture that boundary so cancellation regressions cannot crash the test host.
    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> _callbacks =
            Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
        private int _operations;

        public override void OperationStarted() => Interlocked.Increment(ref _operations);
        public override void OperationCompleted() => Interlocked.Decrement(ref _operations);

        public override void Post(SendOrPostCallback callback, object? state) =>
            _callbacks.Writer.TryWrite((callback, state));

        public void Start(Action action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { action(); }
            finally { SetSynchronizationContext(previous); }
        }

        public Task<List<Exception>> RunPostedCallbacksAsync(TimeSpan timeout) =>
            RunUntilAsync(() => Volatile.Read(ref _operations) == 0, timeout);

        public async Task<List<Exception>> RunUntilAsync(Func<bool> completed, TimeSpan timeout)
        {
            var errors = new List<Exception>();
            using var cancellation = new CancellationTokenSource(timeout);
            while (!completed() || _callbacks.Reader.TryPeek(out _))
            {
                var item = await _callbacks.Reader.ReadAsync(cancellation.Token);
                try { Start(() => item.Callback(item.State)); }
                catch (Exception ex) { errors.Add(ex); }
            }
            return errors;
        }
    }

    private static async Task TrackInstance(DuoSessionMonitor monitor, DuoInstance instance, Action start)
    {
        var tracked = Signal();
        void OnTracked(object? sender, MadWizard.Desomnia.Ressource.Events.InspectableEventArgs<DuoInstance> args)
        {
            if (args.Inspectable == instance) tracked.TrySetResult();
        }
        monitor.TrackingStarted += OnTracked;
        try
        {
            start();
            await tracked.Task.WaitAsync(TestTimeout);
        }
        finally { monitor.TrackingStarted -= OnTracked; }
    }
}
