using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace MadWizard.Desomnia.Network.Manager
{
    internal class CIMNetAdapterPowerManagement : CIMNetAdapterBase, IWakeOnLANManager, IProtocolOffloadManager
    {
        public required ILogger<CIMNetAdapterPowerManagement> Logger { private get; init; }

        OffloadProtocol IProtocolOffloadManager.SupportedProtocols
        {
            get
            {
                var result = OffloadProtocol.None;
                if (this["ArpOffload"] != null)
                    result |= OffloadProtocol.IPv4;
                if (this["NSOffload"] != null)
                    result |= OffloadProtocol.IPv6;

                return result;
            }
        }

        OffloadProtocol IProtocolOffloadManager.OffloadProtocols
        {
            get
            {
                var result = OffloadProtocol.None;
                if (this["ArpOffload"] == true)
                    result |= OffloadProtocol.IPv4;
                if (this["NSOffload"] == true)
                    result |= OffloadProtocol.IPv6;

                return result;
            }

            set
            {
                var supported = ((IProtocolOffloadManager)this).SupportedProtocols;
                var unsupported = value & ~supported;

                if (unsupported != OffloadProtocol.None)
                    throw new NotSupportedException($"Protocol offload is not supported for {unsupported}.");

                if (supported.HasFlag(OffloadProtocol.IPv4))
                    this["ArpOffload"] = value.HasFlag(OffloadProtocol.IPv4);
                if (supported.HasFlag(OffloadProtocol.IPv6))
                    this["NSOffload"] = value.HasFlag(OffloadProtocol.IPv6);
            }
        }

        WakeOnLANMode IWakeOnLANManager.SupportedModes
        {
            get
            {
                var result = WakeOnLANMode.None;
                if (this["WakeOnMagicPacket"] != null)
                    result |= WakeOnLANMode.MagicPacket;
                if (this["WakeOnPattern"] != null)
                    result |= Pattern;

                return result;
            }
        }

        WakeOnLANMode IWakeOnLANManager.Modes
        {
            get
            {
                var result = WakeOnLANMode.None;
                if (this["WakeOnMagicPacket"] == true)
                    result |= WakeOnLANMode.MagicPacket;
                if (this["WakeOnPattern"] == true)
                    result |= Pattern;

                return result;
            }

            set
            {
                this["WakeOnMagicPacket"] = value.HasFlag(WakeOnLANMode.MagicPacket);
                this["WakeOnPattern"] = (value & Pattern) != WakeOnLANMode.None;
            }
        }

        #region CIM access
        private CimInstance PowerManagementInstance
        {
            get
            {
                foreach (var instance in Session.EnumerateInstances(AdapterNamespace, "MSFT_NetAdapterPowerManagementSettingData"))
                    if (string.Equals((string?)instance.CimInstanceProperties["Name"]?.Value, PhysicalAdapterName, StringComparison.OrdinalIgnoreCase))
                        return RefreshInstance(instance);

                throw new InvalidOperationException("MSFT_NetAdapterPowerManagement not found");
            }
        }

        private bool? this[string propertyName]
        {
            get
            {
                if (PowerManagementInstance.CimInstanceProperties[propertyName]?.Value is UInt32 value)
                {
                    switch (value)
                    {
                        case 0:
                            return null;
                        case 1:
                            return false;
                        case 2:
                            return true;

                        default:
                            throw new ArgumentOutOfRangeException(propertyName, value, "Unknown power management setting");
                    }
                }

                throw new InvalidOperationException($"{propertyName} is missing or has an invalid value");
            }

            set
            {
                CimInstance instance = PowerManagementInstance;

                if (instance.CimInstanceProperties[propertyName] is CimProperty property)
                {
                    uint target = (uint)(value ?? throw new ArgumentNullException(nameof(value)) ? 2 : 1);

                    if (!property.Value.Equals(target))
                    {
                        property.Value = target;

                        Session.ModifyInstance(AdapterNamespace, instance);

                        Logger.LogTrace("MSFT_NetAdapterPowerManagement.{Property} = {Value}", propertyName, value);
                    }
                }
                else
                    throw new InvalidOperationException($"{propertyName} not found");
            }
        }
        #endregion
    }
}
