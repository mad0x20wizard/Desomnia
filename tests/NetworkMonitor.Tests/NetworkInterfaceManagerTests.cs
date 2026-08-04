using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    /// <summary>
    /// The reconcile logic of the abstract <see cref="NetworkInterfaceManager"/>, driven
    /// through a fake platform subclass: took-down bookkeeping, tolerant vs enforced foreign
    /// re-enables, the gone-interface skip, the dispose self-heal, and the identity guarantee
    /// across a disconnect.
    /// </summary>
    public class NetworkInterfaceManagerTests
    {
        [Fact]
        public void Disable_TakesTheUpStateAway_AndReleaseRestoresIt()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("eth0"));
            manager.Pump();

            var eth0 = manager.Single();

            eth0.ShouldBeDisabled = true;

            Assert.Equal(["eth0"], manager.DisabledCalls);
            Assert.True(eth0.ShouldBeDisabled);

            manager.Pump(); // the OS now reports the interface down

            eth0.ShouldBeDisabled = false;

            Assert.Equal(["eth0"], manager.EnabledCalls); // what we took away comes back
        }

        [Fact]
        public void AlreadyDisabledInterface_StaysDisabledOnRelease()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("eth0") { Status = OperationalStatus.Down }); // the base reads Down as already disabled
            manager.Pump();

            var eth0 = manager.Single();

            eth0.ShouldBeDisabled = true;

            Assert.Equal(["eth0"], manager.DisabledCalls); // the disable is still asserted

            eth0.ShouldBeDisabled = false;

            Assert.Empty(manager.EnabledCalls); // only a state we actually took away is restored
        }

        [Fact]
        public void DisconnectedButEnabledInterface_IsRestoredOnRelease()
        {
            // the Windows WiFi case: a disconnected adapter enumerates as Down, exactly like a
            // disabled one — but the platform's admin check knows it is still ENABLED, so
            // disabling it does take an enabled interface out of service, and the release (and
            // the shutdown self-heal) must put it back
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("wlan0") { Status = OperationalStatus.Down });
            manager.Enabled.Add("wlan0"); // administratively enabled, merely not associated
            manager.Pump();

            var wlan0 = manager.Single();

            wlan0.ShouldBeDisabled = true;

            Assert.Equal(["wlan0"], manager.DisabledCalls);

            wlan0.ShouldBeDisabled = false;

            Assert.Equal(["wlan0"], manager.EnabledCalls); // restored, though it was Down when disabled
        }

        [Fact]
        public void ForeignReEnable_IsToleratedByDefault()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            var system = new SystemInterface("eth0");
            manager.System.Add(system);
            manager.Pump();

            var eth0 = manager.Single();

            eth0.ShouldBeDisabled = true;
            manager.Pump();

            system.Status = OperationalStatus.Up; // the user flips it back on
            manager.Pump();

            Assert.Equal(["eth0"], manager.DisabledCalls); // tolerated — no second disable
        }

        [Fact]
        public void ForeignReEnable_IsAnsweredWhenEnforced()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            var system = new SystemInterface("eth0");
            manager.System.Add(system);
            manager.Pump();

            var eth0 = manager.Single();

            eth0.EnforceDisabled = true;
            eth0.ShouldBeDisabled = true;
            manager.Pump();

            system.Status = OperationalStatus.Up; // the user flips it back on
            manager.Pump();

            Assert.Equal(["eth0", "eth0"], manager.DisabledCalls); // re-asserted

            eth0.ShouldBeDisabled = false;

            Assert.Equal(["eth0"], manager.EnabledCalls); // the re-assert took an Up state away
        }

        [Fact]
        public void GoneInterface_IsSkippedOnRelease()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("eth0"));
            manager.Pump();

            var eth0 = manager.Single();

            eth0.ShouldBeDisabled = true;

            manager.System.Clear(); // the dock NIC vanishes across sleep
            manager.Pump();

            Assert.Null(manager[eth0.Identity]); // gone from the enumeration

            eth0.ShouldBeDisabled = false;

            Assert.Empty(manager.EnabledCalls); // its imposed state died with it
        }

        [Fact]
        public void HiddenAdapter_IsStillRestored() // the Windows behavior: disabling hides the adapter
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("wlan0"));
            manager.HiddenButKnown.Add("wlan0"); // the platform lookup sees disabled adapters
            manager.Pump();

            var wlan0 = manager.Single();

            wlan0.ShouldBeDisabled = true;

            manager.System.Clear(); // the disable dropped it from the enumeration
            manager.Pump();

            Assert.Null(manager[wlan0.Identity]); // gone from the enumeration — but the intent holds the handle

            wlan0.ShouldBeDisabled = false;

            Assert.Equal(["wlan0"], manager.EnabledCalls); // restored though not enumerated
        }

        [Fact]
        public void HiddenAdapter_ForeignReEnableIsTolerated()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            var system = new SystemInterface("wlan0");
            manager.System.Add(system);
            manager.HiddenButKnown.Add("wlan0");
            manager.Pump();

            var wlan0 = manager.Single();

            wlan0.ShouldBeDisabled = true;

            manager.System.Clear(); // hidden by the disable
            manager.Pump();

            system.Status = OperationalStatus.Up;
            manager.System.Add(system); // a foreign re-enable brings it back
            manager.Pump();

            Assert.Same(wlan0, manager.Single()); // the SAME instance rebinds
            Assert.Equal(["wlan0"], manager.DisabledCalls); // tolerated — the disable stood, no re-assert

            wlan0.EnforceDisabled = true; // enforcement catches up at once

            Assert.Equal(["wlan0", "wlan0"], manager.DisabledCalls);
        }

        [Fact]
        public void Dispose_RestoresWhatItTookAway()
        {
            var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("eth0"));
            manager.System.Add(new SystemInterface("eth1") { Status = OperationalStatus.Down });
            manager.Pump();

            foreach (var @interface in manager.ToList())
                @interface.ShouldBeDisabled = true;

            manager.Dispose();

            Assert.Equal(["eth0"], manager.EnabledCalls); // eth1 was already down — left down
        }

        [Fact]
        public void ReturningInterface_IsTheSameInstance_WithItsIntent()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("eth0"));
            manager.Pump();

            var eth0 = manager.Single();

            eth0.ShouldBeDisabled = true;

            List<INetworkInterface> attached = [], detached = [];
            manager.InterfaceAttached += (_, i) => attached.Add(i);
            manager.InterfaceDetached += (_, i) => detached.Add(i);

            manager.System.Clear(); // physically gone (Unix-style: gone means gone)
            manager.Pump();

            Assert.Equal([eth0], detached);
            Assert.Null(manager[eth0.Identity]);
            Assert.True(eth0.ShouldBeDisabled); // the intent survives the disconnect

            manager.System.Add(new SystemInterface("eth0")); // its successor re-enumerates
            manager.Pump();

            Assert.Equal([eth0], attached); // the identity guarantee: the same instance
            Assert.Same(eth0, manager[eth0.Identity]); // present again, the same instance
            Assert.True(eth0.ShouldBeDisabled);

            // the old imposed state died with the device, so the standing intent is applied
            // afresh to the returning interface
            Assert.Equal(["eth0", "eth0"], manager.DisabledCalls);
        }

        [Fact]
        public void DisposedManager_IgnoresLateChangesAndIntents()
        {
            var manager = new FakeManager() { Logger = NullLogger.Instance };
            var system = new SystemInterface("eth0");
            manager.System.Add(system);
            manager.Pump();

            var eth0 = manager.Single();

            eth0.EnforceDisabled = true;
            eth0.ShouldBeDisabled = true;
            manager.Pump(); // the OS shows the disable now

            manager.Dispose();

            Assert.Equal(["eth0"], manager.EnabledCalls); // the self-heal restored it

            // a NetworkChange already dispatched when Dispose ran lands late — it must not
            // find the enforce branch and take the interface down again with nobody left to heal
            manager.Pump();

            Assert.Equal(["eth0"], manager.DisabledCalls);

            eth0.ShouldBeDisabled = true; // a late intent has no manager left to act for it

            Assert.False(eth0.ShouldBeDisabled);
            Assert.Equal(["eth0"], manager.DisabledCalls);
        }

        [Fact]
        public void IntentHeldHandle_DoesNotAccumulateInTheMemory()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            var system = new SystemInterface("wlan0");
            manager.System.Add(system);
            manager.HiddenButKnown.Add("wlan0");
            manager.Pump();

            var wlan0 = manager.Single();

            wlan0.ShouldBeDisabled = true;

            for (int i = 0; i < 3; i++) // Windows: every applied disable detaches the adapter
            {
                manager.System.Clear();
                manager.Pump();

                system.Status = OperationalStatus.Up; // a foreign re-enable brings it back
                manager.System.Add(system);
                manager.Pump();
            }

            Assert.Same(wlan0, manager.Single());
            Assert.Equal(0, manager.RememberedCount); // the strong hold recalls it — a memory entry would only orphan
        }

        [Fact]
        public void DetachedHandle_WithoutIntent_IsRemembered()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };
            manager.System.Add(new SystemInterface("eth0"));
            manager.Pump();

            var eth0 = manager.Single();

            manager.System.Clear();
            manager.Pump();

            Assert.Equal(1, manager.RememberedCount); // still referenced here — the weak entry is live

            GC.KeepAlive(eth0);
        }

        [Fact]
        public void Refresh_RaisesChanged()
        {
            using var manager = new FakeManager() { Logger = NullLogger.Instance };

            int raised = 0;
            manager.Changed += (_, _) => raised++;

            manager.System.Add(new SystemInterface("eth0"));
            manager.Pump();

            Assert.Equal(1, raised);
        }

        /// <summary>Just enough of the BCL snapshot for the manager's rebind.</summary>
        private sealed class SystemInterface(string id) : NetworkInterface
        {
            public OperationalStatus Status { get; set; } = OperationalStatus.Up;

            public override string Id => id;

            public override string Name => id;

            public override OperationalStatus OperationalStatus => Status;

            public override NetworkInterfaceType NetworkInterfaceType => NetworkInterfaceType.Ethernet;

            public override PhysicalAddress GetPhysicalAddress() => PhysicalAddress.None;

            public override IPInterfaceProperties GetIPProperties() => throw new NetworkInformationException();

            public override bool Supports(NetworkInterfaceComponent networkInterfaceComponent) => false;
        }

        /// <summary>A platform over a fabricated enumeration: disabling and enabling flip the
        /// snapshot's status like an OS would, and <see cref="HiddenButKnown"/> mimics the
        /// Windows lookup that finds adapters their own disable hid.</summary>
        private sealed class FakeManager: NetworkInterfaceManager
        {
            public List<SystemInterface> System { get; } = [];

            public List<string> DisabledCalls { get; } = [];
            public List<string> EnabledCalls { get; } = [];

            public HashSet<string> HiddenButKnown { get; } = [];

            /// <summary>Ids the platform's admin check reports as ENABLED even while they read
            /// Down — the disconnected-WiFi case the BCL status cannot tell from a disable.</summary>
            public HashSet<string> Enabled { get; } = [];

            public void Pump() => Refresh();

            protected override IEnumerable<NetworkInterface> QueryInterfaces() => System.ToArray();

            protected override bool StillExists(INetworkInterface @interface)
                => HiddenButKnown.Contains(@interface.Identity.Id) || base.StillExists(@interface);

            protected override bool IsInterfaceDisabled(INetworkInterface @interface)
                => !Enabled.Contains(@interface.Identity.Id) && base.IsInterfaceDisabled(@interface);

            protected override void DisableInterface(INetworkInterface @interface)
            {
                DisabledCalls.Add(@interface.Identity.Id);

                if (System.FirstOrDefault(system => system.Id == @interface.Identity.Id) is SystemInterface snapshot)
                    snapshot.Status = OperationalStatus.Down;
            }

            protected override void EnableInterface(INetworkInterface @interface)
            {
                EnabledCalls.Add(@interface.Identity.Id);

                if (System.FirstOrDefault(system => system.Id == @interface.Identity.Id) is SystemInterface snapshot)
                    snapshot.Status = OperationalStatus.Up;
            }
        }
    }
}
