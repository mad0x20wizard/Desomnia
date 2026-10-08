using MadWizard.Desomnia.Network.Interface.Manager;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class NetworkInterfaceManagerTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReleaseRestoresOriginalAdministrativeState(bool original, bool forced)
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0", original);
        var nic = manager.Single();
        nic.ShouldBeDisabled = forced;
        Assert.Equal(forced, nic.IsDisabled);
        nic.ShouldBeDisabled = null;
        Assert.Equal(original, adapter.Disabled);
        Assert.Null(nic.ShouldBeDisabled);
    }

    [Fact]
    public void SwitchingForcedValuePreservesOriginalBaseline()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        nic.ShouldBeDisabled = false;
        nic.ShouldBeDisabled = true;
        nic.ShouldBeDisabled = null;
        Assert.False(adapter.Disabled);
    }

    [Fact]
    public void ExternalChangeClearsForcedStateWithoutRevertingOrEnforcing()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0", true);
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        adapter.Disabled = false;
        manager.Pump();
        Assert.Null(nic.ShouldBeDisabled);
        Assert.False(nic.IsDisabled);
        Assert.Empty(manager.Writes);
    }

    [Fact]
    public void ReenforcementRestoresLatestExternalDesireAtShutdown()
    {
        var manager = new TestManager();
        var adapter = manager.Add("eth0", true);
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        adapter.Disabled = false;
        manager.Pump();
        nic.ShouldBeDisabled = true;
        manager.Dispose();
        Assert.False(adapter.Disabled); // latest external state, not startup's disabled state
        Assert.Null(nic.ShouldBeDisabled);
    }

    [Fact]
    public void ReleaseDetectsExternalChangeEvenWithoutNetworkNotification()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0", true);
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        adapter.Disabled = false;
        nic.ShouldBeDisabled = null;
        Assert.False(adapter.Disabled);
        Assert.Empty(manager.Writes);
    }

    [Fact]
    public void OperationalLinkLossDoesNotClearAdministrativeOverride()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        var nic = manager.Single();
        nic.ShouldBeDisabled = false;
        adapter.LinkUp = false;
        manager.Pump();
        Assert.False(nic.IsDisabled);
        Assert.Equal(false, nic.ShouldBeDisabled);
        Assert.Equal(OperationalStatus.Down, nic.Status);
    }

    [Fact]
    public void DelayedOwnWriteIsNotAnExternalChange()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        var nic = manager.Single();
        manager.DelayWrites = true;
        Assert.Throws<InvalidOperationException>(() => nic.ShouldBeDisabled = true);
        manager.Pump();
        Assert.Equal(true, nic.ShouldBeDisabled);
        adapter.Disabled = true; // OS acknowledges the pending write
        manager.Pump();
        Assert.Equal(true, nic.ShouldBeDisabled);
        manager.DelayWrites = false;
        nic.ShouldBeDisabled = null;
        Assert.False(adapter.Disabled);
    }

    [Fact]
    public void PartialWriteFailureStillRestoresBaseline()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        var nic = manager.Single();
        manager.FailAfterWrite = true;
        Assert.Throws<InvalidOperationException>(() => nic.ShouldBeDisabled = true);
        nic.ShouldBeDisabled = null;
        Assert.False(adapter.Disabled);
    }

    [Fact]
    public void FailedReleaseCanBeRetriedWithoutLosingBaseline()
    {
        using var manager = new TestManager();
        var adapter = manager.Add("eth0");
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        manager.FailBeforeWrite = true;
        Assert.Throws<InvalidOperationException>(() => nic.ShouldBeDisabled = null);
        nic.ShouldBeDisabled = null;
        Assert.False(adapter.Disabled);
        Assert.Null(nic.ShouldBeDisabled);
    }

    [Fact]
    public void DisabledAdapterRemainsPresentAndKeepsItsHandle()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        manager.Pump();
        Assert.Same(nic, manager[nic.Identity]);
        Assert.True(nic.IsDisabled);
    }

    [Fact]
    public void GenuineReattachmentPreservesIdentityButStartsWithFreshBaseline()
    {
        using var manager = new TestManager();
        manager.Add("eth0");
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        manager.System.Clear();
        manager.Pump();
        Assert.Empty(manager);
        Assert.Null(nic.ShouldBeDisabled);
        var replacement = manager.Add("eth0", true);
        Assert.Same(nic, manager.Single());
        nic.ShouldBeDisabled = false;
        nic.ShouldBeDisabled = null;
        Assert.True(replacement.Disabled);
    }

    [Fact]
    public void LateWritesAndEventsCannotUndoShutdownRestoration()
    {
        var manager = new TestManager();
        var adapter = manager.Add("eth0");
        var nic = manager.Single();
        nic.ShouldBeDisabled = true;
        manager.Dispose();
        nic.ShouldBeDisabled = true;
        manager.Pump();
        Assert.False(adapter.Disabled);
        Assert.Null(nic.ShouldBeDisabled);
    }

    [Fact]
    public void ExplicitRefreshDoesNotQueueAnotherObserverRound()
    {
        using var manager = new TestManager();
        int changed = 0;
        manager.Changed += (_, _) => changed++;
        manager.Add("eth0");
        ((INetworkInterfaceManager)manager).Refresh();
        Assert.Equal(1, changed);
    }

    internal sealed class TestAdapter(string id, bool disabled = false) : NetworkInterface
    {
        public bool Disabled = disabled;
        public bool LinkUp = true;
        public override string Id => id;
        public override string Name => id;
        public override OperationalStatus OperationalStatus => Disabled || !LinkUp ? OperationalStatus.Down : OperationalStatus.Up;
        public override NetworkInterfaceType NetworkInterfaceType => NetworkInterfaceType.Ethernet;
        public override PhysicalAddress GetPhysicalAddress() => PhysicalAddress.None;
        public override IPInterfaceProperties GetIPProperties() => throw new NetworkInformationException();
        public override bool Supports(NetworkInterfaceComponent component) => false;
    }

    internal sealed class TestManager : NetworkInterfaceManager
    {
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public TestManager() { Logger = NullLogger.Instance; }
        public List<TestAdapter> System { get; } = [];
        public List<(string Id, bool Disabled)> Writes { get; } = [];
        public bool DelayWrites, FailBeforeWrite, FailAfterWrite;
        public string? FailReadId;
        public Action? OnWrite;
        public TestAdapter Add(string id, bool disabled = false)
        {
            var adapter = new TestAdapter(id, disabled);
            System.Add(adapter);
            Pump();
            return adapter;
        }
        public void Pump() => Refresh();
        protected override IEnumerable<NetworkInterface> QueryInterfaces() => System.ToArray();
        protected override bool IsInterfaceDisabled(INetworkInterface nic) => nic.Identity.Id == FailReadId
            ? throw new InvalidOperationException("OS state read failed")
            : System.Single(n => n.Id == nic.Identity.Id).Disabled;
        protected override void DisableInterface(INetworkInterface nic) => Write(nic, true);
        protected override void EnableInterface(INetworkInterface nic) => Write(nic, false);
        private void Write(INetworkInterface nic, bool disabled)
        {
            if (FailBeforeWrite) { FailBeforeWrite = false; throw new InvalidOperationException("OS write failed"); }
            Writes.Add((nic.Identity.Id, disabled));
            if (!DelayWrites) System.Single(n => n.Id == nic.Identity.Id).Disabled = disabled;
            OnWrite?.Invoke();
            if (FailAfterWrite) { FailAfterWrite = false; throw new InvalidOperationException("OS write partially succeeded"); }
        }
    }
}
