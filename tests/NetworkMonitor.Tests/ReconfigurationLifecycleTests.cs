using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Power.Guard;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SharpPcap;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class ReconfigurationLifecycleTests
{
    [Theory]
    [InlineData(CaptureStoppedEventStatus.ErrorWhileCapturing)]
    [InlineData(CaptureStoppedEventStatus.CompletedWithoutError)]
    public void DeviceRestartsUnexpectedCaptureStopsWithoutWatchdog(CaptureStoppedEventStatus status)
    {
        using var fixture = new Fixture();
        fixture.Device.StartCapture();
        fixture.Native.StopUnexpectedly(status);
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal(2, fixture.Native.StartCalls);
        fixture.Device.StopCapture();
        Assert.False(fixture.Device.IsCapturing);
        Assert.Equal(2, fixture.Native.StartCalls);
    }

    [Fact]
    public void DevicePreservesFilterAcrossFailedReopen()
    {
        using var fixture = new Fixture();
        fixture.Native.FailNextOpen = true;
        Assert.Throws<InvalidOperationException>(() => fixture.Device.Restart());
        fixture.Device.Restart();
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal("arp", fixture.Native.Filter);
    }

    [Fact]
    public async Task NestedProtectionPreservesDownContextUntilLastScopeEnds()
    {
        using var fixture = new Fixture();
        var first = fixture.Context.Protect();
        var second = fixture.Context.Protect();
        fixture.Interface.Status = OperationalStatus.Down;
        await fixture.Configure();
        first.Dispose();
        first.Dispose(); // idempotent disposal must not release the second scope
        await fixture.Configure();
        Assert.True(fixture.Context.IsProtected);
        Assert.Single(fixture.Observer);
        second.Dispose();
        await fixture.Configure();
        Assert.True(fixture.Context.Disposed);
        Assert.Equal(NetworkShutdownReason.InterfaceDisconnected, fixture.Service.Reason);
    }

    [Fact]
    public async Task InterfaceEnforcementContinuesIndependentlyOfContextProtection()
    {
        using var fixture = new Fixture(rules: [new() { Name = "other", Disabled = true }]);
        using var protection = fixture.Context.Protect();
        var other = new FakeNetworkInterface("other");
        fixture.Manager.Attach(other);
        await fixture.Configure();
        Assert.True(other.IsDisabled);
        Assert.Null(fixture.Interface.ShouldBeDisabled);
        Assert.False(fixture.Context.Disposed);
    }

    [Fact]
    public async Task UnrelatedDisconnectionIsReconciledWhileContextIsProtected()
    {
        using var fixture = new Fixture();
        using var other = new Fixture("other");
        fixture.Manager.Attach(other.Interface);
        fixture.Contexts.Add(new(other.Context, other.Context));
        using var protection = fixture.Context.Protect();
        other.Interface.Status = OperationalStatus.Down;
        await fixture.Configure();
        Assert.True(other.Context.Disposed);
        Assert.False(fixture.Context.Disposed);
        Assert.Single(fixture.Observer);
    }

    [Fact]
    public async Task UnknownAdministrativeStateRetainsAnOtherwiseOperationalContext()
    {
        using var fixture = new Fixture();
        fixture.Interface.FailDisabledRead = true;
        await fixture.Configure();
        Assert.Empty(fixture.Interfaces.GetAvailableInterfaces());
        Assert.Single(fixture.Observer);
        Assert.False(fixture.Context.Disposed);
        fixture.Interface.Status = OperationalStatus.Down;
        await fixture.Configure();
        Assert.True(fixture.Context.Disposed);
        Assert.Equal(NetworkShutdownReason.InterfaceDisconnected, fixture.Service.Reason);
    }

    [Fact]
    public async Task ProtectedContextSurvivesInterfaceReattachment()
    {
        using var fixture = new Fixture();
        var protection = fixture.Context.Protect();
        fixture.Manager.Detach(fixture.Interface);
        fixture.Manager.Attach(fixture.Interface);
        await fixture.Configure();
        Assert.False(fixture.Context.Disposed);
        protection.Dispose();
        await fixture.Configure();
        Assert.False(fixture.Context.Disposed);
        Assert.Same(fixture.Context, Assert.Single(fixture.Contexts).Value);
        Assert.Equal(0, fixture.Service.Shutdowns);
    }

    [Fact]
    public async Task CoalescedDisconnectAndReconnectPreserveAnUnprotectedContext()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool first = true;
        fixture.Interfaces.Changed += async (_, _) =>
        {
            if (!first) return;
            first = false;
            entered.TrySetResult();
            await release.Task;
        };
        var round = fixture.Configure();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Manager.Detach(fixture.Interface);
            fixture.Manager.Attach(fixture.Interface);
        }
        finally { release.TrySetResult(); }
        await round;
        await fixture.Configure();

        Assert.False(fixture.Context.Disposed);
        Assert.Same(fixture.Context, Assert.Single(fixture.Contexts).Value);
        Assert.Equal(0, fixture.Service.Shutdowns);
    }

    [Fact]
    public async Task SuspendedContextResumesAfterInterfaceDisappearsAndReturns()
    {
        using var fixture = new Fixture();
        await fixture.Invoke("SuspendMonitoring");
        fixture.Manager.Detach(fixture.Interface);
        await fixture.Configure();
        Assert.False(fixture.Context.Disposed);
        Assert.Empty(fixture.Interfaces.GetAvailableInterfaces());
        fixture.Manager.Attach(fixture.Interface);
        await fixture.Configure();

        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.OnResume = () => { resumed.TrySetResult(); return Task.CompletedTask; };
        await fixture.Invoke("ResumeMonitoring");
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await fixture.Configure();

        Assert.Same(fixture.Context, Assert.Single(fixture.Contexts).Value);
        Assert.False(fixture.Context.Disposed);
        Assert.False(fixture.Context.IsSuspended);
        Assert.Equal(1, fixture.Service.Resumes);
        Assert.Equal(0, fixture.Service.Shutdowns);
    }

    [Fact]
    public async Task InterfaceChangesWaitForPreparationAndUseRecoveredState()
    {
        using var fixture = new Fixture();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.OnBeforeSuspend = () => finish.Task;
        var prepare = ((IPowerTransitionGuard)fixture.Observer).BeforeTransition(PowerTransition.Suspend);
        Assert.True(fixture.Context.IsProtected);
        fixture.Interface.Status = OperationalStatus.Down;
        var change = fixture.Configure();
        Assert.False(change.IsCompleted);
        fixture.Interface.Status = OperationalStatus.Up;
        finish.SetResult();
        await Task.WhenAll(prepare, change).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(fixture.Observer);
        Assert.False(fixture.Context.IsProtected);
        Assert.Equal(0, fixture.Service.Shutdowns);
    }

    [Fact]
    public async Task AbortedPreparationReleasesProtectionAndRestartsJanitor()
    {
        using var fixture = new Fixture();
        fixture.Service.OnBeforeSuspend = () => throw new InvalidOperationException("veto");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IPowerTransitionGuard)fixture.Observer).BeforeTransition(PowerTransition.Suspend));
        Assert.False(fixture.Context.IsProtected);
        Assert.Throws<Exception>(() => fixture.Janitor.StartSweeping());
        fixture.Service.OnBeforeSuspend = null;
        await ((IPowerTransitionGuard)fixture.Observer).BeforeTransition(PowerTransition.Suspend);
    }

    [Fact]
    public async Task SuspendWaitsForPreparationAndTeardownWaitsForSuspendCallback()
    {
        using var fixture = new Fixture();
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var suspended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.OnBeforeSuspend = () => prepared.Task;
        fixture.Service.OnSuspend = () => { entered.SetResult(); return suspended.Task; };
        var prepare = ((IPowerTransitionGuard)fixture.Observer).BeforeTransition(PowerTransition.Suspend);
        var suspend = fixture.Invoke("SuspendMonitoring");
        Assert.False(suspend.IsCompleted);
        Assert.Equal(0, fixture.Service.Suspends);
        prepared.SetResult();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = fixture.Invoke("UnconfigureNetworkMonitors");
        Assert.False(stop.IsCompleted);
        Assert.Equal(0, fixture.Service.Shutdowns);
        suspended.SetResult();
        await Task.WhenAll(prepare, suspend, stop).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(NetworkShutdownReason.ApplicationShutdown, fixture.Service.Reason);
        Assert.True(fixture.Context.Disposed);
    }

    [Fact]
    public async Task ResumeCallbackIsAwaitedBeforeTeardown()
    {
        using var fixture = new Fixture();
        await fixture.Invoke("SuspendMonitoring");
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.OnResume = () => { entered.SetResult(); return resumed.Task; };
        var resume = fixture.Invoke("ResumeMonitoring");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(fixture.Context.IsProtected);
        var stop = fixture.Invoke("UnconfigureNetworkMonitors");
        Assert.False(stop.IsCompleted);
        resumed.SetResult();
        await Task.WhenAll(resume, stop).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(fixture.Context.IsProtected);
        Assert.Equal(1, fixture.Service.Resumes);
        Assert.Equal(1, fixture.Service.Shutdowns);
    }

    [Fact]
    public async Task SuspendedContextReleasesOnlyItsOwnProtection()
    {
        using var fixture = new Fixture();
        using var outer = fixture.Context.Protect();
        await fixture.Context.Suspend();
        Assert.True(fixture.Context.IsSuspended);
        fixture.Context.EndSuspension();
        Assert.False(fixture.Context.IsSuspended);
        Assert.True(fixture.Context.IsProtected);
        Assert.False(fixture.Device.IsCapturing);
    }
    // Build the observer's live graph without a native adapter or production discovery.
    // The context constructor normally creates that graph through Autofac; only its cached
    // properties are needed here. Disposal is overridden to avoid owning the test graph twice.
    internal sealed class Fixture : IDisposable
    {
        public FakeNetworkInterface Interface = new("test");
        public NetworkDevice Device;
        public NativeDevice Native;
        public NetworkMonitor Monitor;
        public NetworkJanitor Janitor;
        public CountingService Service = new();
        public TestContext Context;
        public DynamicNetworkObserver Observer;
        public FakeNetworkInterfaceManager Manager;
        public NetworkInterfaceMonitor Interfaces;
        public List<NetworkInterfaceWatchInfo> InterfaceConfigurations = [];
        public IList<Owned<NetworkContext>> Contexts => (IList<Owned<NetworkContext>>)typeof(DynamicNetworkObserver)
            .GetField("_contexts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Observer)!;
        public Fixture(string id = "test", NetworkInterfaceWatchInfo[]? rules = null)
        {
            Interface = new(id);
            var live = DispatchProxy.Create<ILiveDevice, NativeDevice>();
            Native = (NativeDevice)live;
            Device = new(NullLogger<NetworkDevice>.Instance, Interface, live);
            Device.Filter = "arp";
            var network = new NetworkSegment { Logger = NullLogger<NetworkSegment>.Instance,
                Device = Device, LocalRange = null!, Ranges = null! };
            Janitor = new(new SweepOptions { Frequency = TimeSpan.FromHours(1) })
                { Logger = NullLogger<NetworkJanitor>.Instance, Network = network };
            Janitor.StartSweeping();
            Monitor = new() { Logger = NullLogger<NetworkMonitor>.Instance, Name = "test",
                Device = Device, Network = network, Janitor = Janitor, Options = default, Services = [Service] };
            var config = new NetworkMonitorConfig { Name = id, Interface = id };
            Context = (TestContext)RuntimeHelpers.GetUninitializedObject(typeof(TestContext));
            SetContext("Config", config);
            SetContext("Device", Device);
            SetContext("Monitor", Monitor);
            Manager = new(Interface);
            if (rules is not null) InterfaceConfigurations.AddRange(rules);
            Interfaces = new(InterfaceConfigurations) { Manager = Manager, CreateMatcher = () => new InterfaceMatcher(), Logger = NullLogger<NetworkInterfaceMonitor>.Instance };
            Observer = new()
            {
                Logger = NullLogger<DynamicNetworkObserver>.Instance, Power = new FakePowerManager(), Monitor = Interfaces,
                Selector = new NetworkConfigSelector(() => new InterfaceMatcher(), [config]),
                CreateContext = (_, _) => throw new Xunit.Sdk.XunitException("Existing context must not be recreated")
            };
            Contexts.Add(new(Context, Context));
            Task.Run(() => ((IHostedService)Observer).StartAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }
        public void SetServices(params INetworkService[] services) => typeof(NetworkMonitor).GetProperty(nameof(NetworkMonitor.Services))!.SetValue(Monitor, services);
        private void SetContext(string property, object value) => typeof(NetworkContext)
            .GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Context, value);
        public Task Configure() => NetworkInterfaceMonitorTests.Reconcile(Interfaces);
        public Task Invoke(string name)
        {
            if (name == "UnconfigureNetworkMonitors") return ((IHostedService)Observer).StopAsync(CancellationToken.None);
            if (name is "SuspendMonitoring" or "ResumeMonitoring")
            {
                string handler = name == "SuspendMonitoring" ? "PowerManager_Suspended" : "PowerManager_ResumeSuspended";
                typeof(DynamicNetworkObserver).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(Observer, [null, EventArgs.Empty]);
                return Configure(); // queued behind the complete awaited event callback
            }
            return (Task)typeof(DynamicNetworkObserver).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Observer, null)!;
        }
        public void Dispose()
        {
            Task.Run(() => ((IHostedService)Observer).StopAsync(CancellationToken.None)).GetAwaiter().GetResult();
            Interfaces.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Janitor.StopSweeping().GetAwaiter().GetResult();
            ((IDisposable)Device).Dispose();

        }
    }
    internal sealed class TestContext : NetworkContext
    {
        public bool Disposed;
        private TestContext() : base(null!, null!, null!) { }
        public override void Dispose() => Disposed = true;
    }
    internal sealed class CountingService : INetworkService
    {
        public int Suspends, Resumes, Shutdowns;
        public Func<Task>? OnBeforeSuspend, OnSuspend, OnResume;
        public Task BeforeSuspend() => OnBeforeSuspend?.Invoke() ?? Task.CompletedTask;
        public NetworkShutdownReason? Reason;
        public async Task Suspend() { Suspends++; if (OnSuspend is not null) await OnSuspend(); }
        public async Task Resume() { Resumes++; if (OnResume is not null) await OnResume(); }
        public Task Shutdown(NetworkShutdownReason reason) { Shutdowns++; Reason = reason; return Task.CompletedTask; }
    }
    public class NativeDevice : DispatchProxy
    {
        public int OpenCalls;
        public int StartCalls;
        public bool FailNextOpen;
        public Action? OnStop, OnOpen;
        public string? Filter;
        private bool started;
        private Delegate? stopped;
        public void StopUnexpectedly(CaptureStoppedEventStatus status)
        {
            started = false;
            stopped?.DynamicInvoke(this, status);
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "Open":
                    OpenCalls++; OnOpen?.Invoke();
                    if (FailNextOpen) { FailNextOpen = false; throw new InvalidOperationException("Cannot open yet"); }
                    return null;
                case "get_Name": case "get_Description": return "test";
                case "get_Started": return started;
                case "get_Filter": return Filter;
                case "set_Filter": Filter = (string?)args![0]; return null;
                case "add_OnCaptureStopped": stopped = Delegate.Combine(stopped, (Delegate)args![0]!); return null;
                case "remove_OnCaptureStopped": stopped = Delegate.Remove(stopped, (Delegate)args![0]!); return null;
                case "StartCapture": StartCalls++; started = true; return null;
                case "StopCapture": OnStop?.Invoke();
                    if (started) StopUnexpectedly(CaptureStoppedEventStatus.CompletedWithoutError);
                    return null;
                case "Close": started = false; Filter = null; return null;
                default: return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }
    }
}
