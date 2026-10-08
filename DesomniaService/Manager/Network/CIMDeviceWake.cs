using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace MadWizard.Desomnia.Network.Manager
{
    // see: https://gist.github.com/marvinlehmann/194a95ce14bad67d2680992c20950f79
    internal class CIMDeviceWake : CIMNetAdapterBase, IWakeOnLANManager
    {
        public required ILogger<CIMDeviceWake> Logger { private get; init; }

        WakeOnLANMode IWakeOnLANManager.SupportedModes
        {
            get
            {
                var modes = FindInstance("MSPower_DeviceWakeEnable") is not null
                    ? WakeOnLANMode.Pattern | WakeOnLANMode.MagicPacket
                    : WakeOnLANMode.None;

                return modes;
            }
        }

        WakeOnLANMode IWakeOnLANManager.Modes
        {
            get
            {
                if (!ReadProperty<bool>("MSPower_DeviceWakeEnable", "Enable"))
                    return WakeOnLANMode.None;

                if (ReadProperty<bool>("MSNdis_DeviceWakeOnMagicPacketOnly", "EnableWakeOnMagicPacketOnly"))
                    return WakeOnLANMode.MagicPacket;

                return WakeOnLANMode.Pattern | WakeOnLANMode.MagicPacket;
            }

            set
            {
                bool wake = value != WakeOnLANMode.None;

                WriteProperty("MSPower_DeviceWakeEnable", "Enable", wake);

                var onlyOnMagicPacket = wake && ((value & (value - (int)WakeOnLANMode.MagicPacket)) == 0);

                WriteProperty("MSNdis_DeviceWakeOnMagicPacketOnly", "EnableWakeOnMagicPacketOnly", onlyOnMagicPacket);
            }
        }

        #region CIM/WMI access
        private const string WMINamespace = @"root\wmi";

        private CimInstance? FindInstance(string className)
        {
            foreach (var instance in Session.EnumerateInstances(WMINamespace, className))
                if (((string)instance.CimInstanceProperties["InstanceName"].Value).StartsWith(PhysicalPnpDeviceID))
                    return RefreshInstance(instance);

            return null;
        }

        private T? ReadProperty<T>(string className, string propertyName)
        {
            if (FindInstance(className) is CimInstance instance)
                return (T?)instance.CimInstanceProperties[propertyName].Value;

            return default;
        }

        private void WriteProperty<T>(string className, string propertyName, T value)
        {
            var instance = FindInstance(className) 
                ?? throw new InvalidOperationException($"{className} not found");
            var property = instance.CimInstanceProperties[propertyName] 
                ?? throw new InvalidOperationException($"{propertyName} not found");

            if (!property.Value.Equals(value))
            {
                instance.CimInstanceProperties[propertyName].Value = value;
                Session.ModifyInstance(WMINamespace, instance);

                Logger.LogTrace("{ClassName}.{PropertyName} = {Value}", className, propertyName, value);
            }
        }
        #endregion
    }
}
