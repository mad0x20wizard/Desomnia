using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Interface.Configuration;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Guard;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Fixture = MadWizard.Desomnia.Network.Tests.ReconfigurationLifecycleTests.Fixture;

namespace MadWizard.Desomnia.Network.Tests;

public class InterfaceReconfigurationTests
{
    internal static INetworkService Create(Fixture fixture, params Configurator[] configurations)
        => new InterfaceConfigurator(configurations)
        {
            Logger = NullLogger<InterfaceConfigurator>.Instance,
            Context = fixture.Context, Manager = fixture.Manager
        };

    [Fact]
    public async Task BatchPlansBeforePausingAndRecoversOnceThenRestoresInReverseOrder()
    {
        using var fixture = new Fixture();
        List<string> events = [];
        fixture.Device.StartCapture();
        fixture.Native.OnStop = () => events.Add("pause");
        fixture.Native.OnOpen = () => events.Add("recover");
        var first = new Setting("first", 1, 2, events);
        var second = new Setting("second", 3, 4, events);
        var service = Create(fixture, first, second);

        await service.BeforeSuspend();
        Assert.Equal(["read:first", "read:second", "pause", "write:first:2", "write:second:4", "recover"], events);
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal("arp", fixture.Native.Filter);
        Assert.Equal(0, fixture.Service.Suspends);
        Assert.Equal(0, fixture.Service.Resumes);
        events.Clear();
        await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
        Assert.Equal(["pause", "write:second:3", "write:first:1"], events);
        Assert.False(fixture.Device.IsCapturing);
        events.Clear();
        await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
        Assert.Empty(events);
    }

    [Fact]
    public async Task NoChangesDoesNotPauseRecoverOrRestore()
    {
        using var fixture = new Fixture();
        List<string> events = [];
        fixture.Device.StartCapture();
        var opens = fixture.Native.OpenCalls;
        var service = Create(fixture, new Setting("same", 1, 1, events));
        await service.BeforeSuspend();
        await service.Shutdown(NetworkShutdownReason.InterfaceShutdown);
        Assert.Equal(["read:same"], events);
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal(opens, fixture.Native.OpenCalls);
    }

    [Fact]
    public async Task PartialFailureDoesNotSkipOtherChangesOrLoseOriginal()
    {
        using var fixture = new Fixture();
        List<string> events = [];
        var first = new Setting("first", 1, 2, events) { FailAfterWrite = true };
        var second = new Setting("second", 3, 4, events);
        var service = Create(fixture, first, second);
        await service.BeforeSuspend();
        Assert.Equal(4, second.Value);
        await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
        Assert.Equal(1, first.Value);
        Assert.Equal(3, second.Value);
    }

    [Fact]
    public async Task FailedRestoreDoesNotSkipOtherSettings()
    {
        using var fixture = new Fixture();
        List<string> events = [];
        var first = new Setting("first", 1, 2, events);
        var second = new Setting("second", 3, 4, events);
        var service = Create(fixture, first, second);
        await service.BeforeSuspend();
        second.FailAfterWrite = true;
        await service.Shutdown(NetworkShutdownReason.InterfaceShutdown);
        Assert.Equal(1, first.Value);
        Assert.Equal(3, second.Value);
        Assert.False(fixture.Device.IsCapturing);
    }

    [Fact]
    public async Task DisconnectedShutdownNeverWrites()
    {
        using var fixture = new Fixture();
        List<string> events = [];
        var service = Create(fixture, new Setting("one", 1, 2, events));
        await service.BeforeSuspend();
        events.Clear();
        await service.Shutdown(NetworkShutdownReason.InterfaceDisconnected);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadAndUnsupportedApplyErrorsDoNotSkipOtherConfigurators()
    {
        using var fixture = new Fixture();
        List<string> events = [];
        var supported = new Setting("supported", 1, 2, events);
        var service = Create(fixture,
            new Setting("read", 1, 2, events) { FailRead = true },
            new Setting("unsupported", 1, 2, events) { Unsupported = true }, supported);
        await service.BeforeSuspend();
        Assert.Equal(2, supported.Value);
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal(["read:read", "read:unsupported", "read:supported", "write:unsupported:2", "write:supported:2"], events);
    }

    [Fact]
    public async Task RecoveryTimeoutAbortsPreparationAndReconcilesDisconnectedContext()
    {
        using var fixture = new Fixture();
        var service = Create(fixture, new DisconnectingSetting(fixture));
        bool continued = false;
        fixture.Service.OnBeforeSuspend = () => { continued = true; return Task.CompletedTask; };
        fixture.SetServices(service, fixture.Service);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            ((IPowerTransitionGuard)fixture.Observer)
                .BeforeTransition(PowerTransition.Suspend));

        Assert.False(continued);
        Assert.False(fixture.Context.IsProtected);
        Assert.False(fixture.Device.IsCapturing);
        Assert.True(fixture.Context.Disposed);
        Assert.Equal(NetworkShutdownReason.InterfaceDisconnected, fixture.Service.Reason);
    }

    private sealed class DisconnectingSetting(Fixture fixture) : Configurator<int>(1)
    {
        protected internal override int ReadConfiguration() => 0;
        protected internal override Task ApplyConfiguration(int config)
        {
            fixture.Interface.Status = System.Net.NetworkInformation.OperationalStatus.Down;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WakeOnLanResetsLogicalValueThroughManager()
    {
        using var fixture = new Fixture();
        var manager = new WakeManager();
        var service = Create(fixture, new WakeOnLANConfigurator(WakeOnLANMode.MagicPacket, manager));
        await service.BeforeSuspend();
        // Simulate the driver changing configuration before the next suspend.
        manager.Current = WakeOnLANMode.None;
        await service.BeforeSuspend();
        await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
        Assert.Equal([WakeOnLANMode.MagicPacket, WakeOnLANMode.MagicPacket,
            WakeOnLANMode.MagicPacket | WakeOnLANMode.Pattern], manager.Writes);
    }

    private sealed class WakeManager : IWakeOnLANManager
    {
        public WakeOnLANMode Current = WakeOnLANMode.MagicPacket | WakeOnLANMode.Pattern;
        public List<WakeOnLANMode> Writes = [];
        public WakeOnLANMode SupportedModes => WakeOnLANMode.MagicPacket | WakeOnLANMode.Pattern;
        public WakeOnLANMode Modes { get => Current; set { Writes.Add(value); Current = value; } }
    }

    private sealed class Setting(string name, int original, int target, List<string> events) : Configurator<int>(target)
    {
        public int Value = original;
        public bool FailAfterWrite, FailRead, Unsupported;
        protected internal override int ReadConfiguration()
        {
            events.Add("read:" + name);
            if (FailRead) throw new InvalidOperationException();
            return Value;
        }
        protected internal override Task ApplyConfiguration(int config)
        {
            events.Add($"write:{name}:{config}");
            if (Unsupported) throw new NotSupportedException();
            Value = config;
            if (FailAfterWrite) { FailAfterWrite = false; throw new InvalidOperationException(); }
            return Task.CompletedTask;
        }
    }
}
