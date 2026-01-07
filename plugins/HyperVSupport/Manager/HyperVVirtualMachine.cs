using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Nito.AsyncEx;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal class HyperVVirtualMachine(HyperVManager manager, string guid) : IVirtualMachine
    {
        static readonly TimeSpan STATE_TTL = TimeSpan.FromSeconds(5);

        public required ILogger<HyperVVirtualMachine> Logger { private get; init; }

        readonly MemoryCache _cache = new(new MemoryCacheOptions());

        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        public string Name              => field ??= QueryInstanceProperty<string>("ElementName");
        public PhysicalAddress Address  => field ??= QueryPhysicalAddress() ?? throw new Exception($"No MAC address found for VM '{Name}'.");

        public VirtualMachineState State
        {
            get
            {
                var state = QueryState(STATE_TTL);

                if (field == VirtualMachineState.Unknown)
                {
                    field = state;
                }

                return state;
            }

            internal set
            {
                if (field != value)
                {
                    _cache.Clear();

                    if (State == value)
                    {
                        VirtualMachineStateChangedEventArgs args = new(field, field = value);

                        StateChanged?.Invoke(this, args);
                    }
                }
            }
        }

        public event EventHandler<VirtualMachineStateChangedEventArgs>? StateChanged;

        #region Actions
        public async Task Start()
        {
            using var locked = await Semaphore.LockAsync();

            manager.Logger.LogDebug($"Starting virtual machine '{Name}'...");

            await manager.RequestStateChange(this, RequestedHyperVVMState.Enabled);
        }
        public async Task Suspend()
        {
            using var locked = await Semaphore.LockAsync();

            manager.Logger.LogDebug($"Suspending virtual machine '{Name}'...");

            await manager.RequestStateChange(this, RequestedHyperVVMState.Offline);
        }
        public async Task Stop()
        {
            using var locked = await Semaphore.LockAsync();

            manager.Logger.LogDebug($"Stopping virtual machine '{Name}'...");

            await manager.RequestStateChange(this, RequestedHyperVVMState.Disabled);
        }
        #endregion

        #region CIM Queries
        internal CimInstance QueryInstance()
        {
            var QUERY = $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{guid}'";

            return manager.Session.QueryInstances(HyperVManager.NS, HyperVManager.DIALECT, QUERY).FirstOrDefault()
                ?? throw new InvalidOperationException($"VM '{guid}' not found.");
        }

        internal T QueryInstanceProperty<T>(string propertyName, TimeSpan ttl = default)
        {
            if (ttl > TimeSpan.Zero && _cache.TryGetValue(propertyName, out T? value))
                return value!;

            var QUERY = $"SELECT {propertyName} FROM Msvm_ComputerSystem WHERE Name = '{guid}'";

            var vm = manager.Session.QueryInstances(HyperVManager.NS, HyperVManager.DIALECT, QUERY).FirstOrDefault()
                ?? throw new InvalidOperationException($"VM '{guid}' not found.");

            value = (T)(vm.CimInstanceProperties[propertyName].Value ?? throw new InvalidOperationException($"Property '{propertyName}' not found on VM '{guid}'."));

            if (ttl > TimeSpan.Zero)
                _cache.Set(propertyName, value, ttl);

            return value;
        }

        internal VirtualMachineState QueryState(TimeSpan ttl = default)
        {
            return ((HyperVVMState)QueryInstanceProperty<ushort>("EnabledState", ttl)).ToVMState();
        }

        internal PhysicalAddress? QueryPhysicalAddress()
        {
            const string CLASS_SETTINGS = "Msvm_VirtualSystemSettingData";
            const string CLASS_SETTINGS_PORT = "Msvm_SyntheticEthernetPortSettingData";

            var vm = QueryInstance();

            var settings = manager.Session.EnumerateAssociatedInstances(HyperVManager.NS, vm, CLASS_SETTINGS)
                // there will be multiple settings, if you have snapshots
                .Where(instance => instance.CimInstanceProperties["InstanceID"].Value.ToString()?.Contains(guid) ?? false).FirstOrDefault()
                    ?? throw new InvalidOperationException($"No VirtualSystemSettingData found for VM '{guid}'.");

            var settingsPort = manager.Session.EnumerateAssociatedInstances(HyperVManager.NS, settings, CLASS_SETTINGS_PORT);

            var addresses = settingsPort.Select(nic => nic.CimInstanceProperties["Address"]?.Value as string).Where(mac => mac is not null);

            if (addresses.Count() > 1)
                throw new Exception("Multiple MAC addresses found for VM '{Name}'. You need to specify it explicitly.");

            var phy = addresses.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

            return !string.IsNullOrWhiteSpace(phy) ? PhysicalAddress.Parse(phy) : null;
        }
        #endregion

        public override string ToString()
        {
            return $"HyperVVirtualMachine[name='{Name}', mac={Address.ToHexString()}, state={State}]";
        }
    }
}
