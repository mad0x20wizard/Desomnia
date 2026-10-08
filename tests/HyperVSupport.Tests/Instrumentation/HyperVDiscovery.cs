using HarmonyLib;
using MadWizard.Desomnia.Network.HyperV.Manager;
using MadWizard.Desomnia.Network.Manager;
using SharpPcap;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Network.HyperV.Tests
{
    // Only dependency queries are replaced. The middleware and capture device execute unchanged.
    // Instance tables and an async-local scope keep concurrent tests isolated.
    internal sealed class HyperVDiscovery : IDisposable
    {
        private static readonly AsyncLocal<HyperVDiscovery?> Current = new();
        private static readonly ConditionalWeakTable<HyperVManager, HyperVDiscovery> Managers = new();
        private static readonly ConditionalWeakTable<HyperVSwitch, HyperVDiscovery> Switches = new();
        private static readonly Lazy<bool> Installed = new(Install);
        private readonly HyperVDiscovery? _previous;
        private readonly CaptureDeviceList _devices;
        private readonly HyperVManager _manager;
        private readonly HyperVSwitch _switch;

        internal bool IsSwitch = true;
        internal HyperVSwitchType SwitchType = HyperVSwitchType.External;
        internal (NetworkIdentity Identity, string Name)? PhysicalInterface;
        internal PhysicalAddress[] VirtualMachineAddresses = [];
        internal Exception? DiscoveryError;
        internal Exception? PhysicalInterfaceError;
        internal Exception? VirtualMachineError;
        internal NetworkIdentity? RequestedInterface;
        internal int DeviceEnumerations;
        internal int AddressQueries;

        internal HyperVDiscovery(HyperVManager manager, IEnumerable<ILiveDevice> devices)
        {
            _manager = manager;
            _switch = new HyperVSwitch(manager, Guid.NewGuid(), "Test switch");
            // CaptureDeviceList has no injectable constructor. Supply its normal
            // ReadOnlyCollection storage without running the native discovery constructor.
            _devices = (CaptureDeviceList)RuntimeHelpers.GetUninitializedObject(typeof(CaptureDeviceList));
            AccessTools.Field(typeof(ReadOnlyCollection<ILiveDevice>), "list").SetValue(_devices, devices.ToList());
            Managers.Add(manager, this);
            Switches.Add(_switch, this);
            _previous = Current.Value;
            Current.Value = this;
        }

        [ModuleInitializer]
        internal static void Initialize() => _ = Installed.Value;

        private static bool Install()
        {
            var harmony = new Harmony("Desomnia.HyperV.Tests.Discovery");
            void Patch(MethodBase method, string prefix) =>
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(HyperVDiscovery), prefix));

            Patch(AccessTools.Method(typeof(HyperVManager), nameof(HyperVManager.FindSwitch)), nameof(FindSwitch));
            Patch(AccessTools.PropertyGetter(typeof(HyperVSwitch), nameof(HyperVSwitch.Type)), nameof(GetSwitchType));
            Patch(AccessTools.Method(typeof(HyperVSwitch), nameof(HyperVSwitch.QueryPhysicalInterface)), nameof(QueryPhysicalInterface));
            Patch(AccessTools.Method(typeof(HyperVSwitch), nameof(HyperVSwitch.QueryVirtualMachineAddresses)), nameof(QueryVirtualMachineAddresses));
            Patch(AccessTools.PropertyGetter(typeof(CaptureDeviceList), nameof(CaptureDeviceList.Instance)), nameof(GetDevices));
            return true;
        }

        private static bool FindSwitch(HyperVManager __instance, NetworkIdentity __0, ref HyperVSwitch? __result)
        {
            if (!Managers.TryGetValue(__instance, out var discovery)) return true;
            discovery.RequestedInterface = __0;
            if (discovery.DiscoveryError is { } error) throw error;
            __result = discovery.IsSwitch ? discovery._switch : null;
            return false;
        }

        private static bool GetSwitchType(HyperVSwitch __instance, ref HyperVSwitchType __result)
        {
            if (!Switches.TryGetValue(__instance, out var discovery)) return true;
            __result = discovery.SwitchType;
            return false;
        }

        private static bool QueryPhysicalInterface(HyperVSwitch __instance, ref (NetworkIdentity Identity, string Name)? __result)
        {
            if (!Switches.TryGetValue(__instance, out var discovery)) return true;
            if (discovery.PhysicalInterfaceError is { } error) throw error;
            __result = discovery.PhysicalInterface;
            return false;
        }

        private static bool QueryVirtualMachineAddresses(HyperVSwitch __instance, ref IEnumerable<PhysicalAddress> __result)
        {
            if (!Switches.TryGetValue(__instance, out var discovery)) return true;
            discovery.AddressQueries++;
            __result = ReadAddresses(discovery);
            return false;
        }

        private static IEnumerable<PhysicalAddress> ReadAddresses(HyperVDiscovery discovery)
        {
            // Real discovery is lazy; exercise exceptions during composite construction too.
            if (discovery.VirtualMachineError is { } error) throw error;
            foreach (var address in discovery.VirtualMachineAddresses) yield return address;
        }

        private static bool GetDevices(ref CaptureDeviceList __result)
        {
            if (Current.Value is not { } discovery) return true;
            discovery.DeviceEnumerations++;
            __result = discovery._devices;
            return false;
        }

        public void Dispose()
        {
            Current.Value = _previous;
            Managers.Remove(_manager);
            Switches.Remove(_switch);
        }
    }
}
