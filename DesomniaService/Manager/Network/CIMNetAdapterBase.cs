using Microsoft.Management.Infrastructure;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    internal abstract class CIMNetAdapterBase
    {
        protected const string AdapterNamespace = @"root\StandardCimv2";

        public required NetworkDevice Device { private get; init; }

        protected CimSession Session
        {
            get
            {
                if (field == null || !field.TestConnection())
                {
                    field?.Dispose();
                    field = CimSession.Create(null);
                }

                return field;
            }

            set
            {
                if (value == null)
                {
                    field?.Dispose();
                    field = value;
                }
            }
        }

        private CimInstance PhysicalAdapter
        {
            get
            {
                if (field == null)
                {
                    foreach (var instance in Session.EnumerateInstances(AdapterNamespace, "MSFT_NetAdapter"))
                        if ((bool?)instance.CimInstanceProperties["HardwareInterface"]?.Value ?? false)
                            if (instance.CimInstanceProperties["NetworkAddresses"].Value is string[] addresses &&
                                addresses.Any(a => string.Equals(a, $"{Device.PhysicalAddress}", StringComparison.OrdinalIgnoreCase)))
                                    return field = instance;

                    throw new InvalidOperationException($"No physical network adapter with MAC '{Device.PhysicalAddress.ToHexString()}' found.");
                }

                return field;
            }
        }

        protected string PhysicalAdapterName => field ??= (string)PhysicalAdapter.CimInstanceProperties["Name"].Value;
        protected string PhysicalPnpDeviceID => field ??= (string)PhysicalAdapter.CimInstanceProperties["PnpDeviceID"].Value;

        protected CimInstance RefreshInstance(CimInstance instance)
        {
            var key = new CimInstance(instance.CimSystemProperties.ClassName, instance.CimSystemProperties.Namespace);

            foreach (var property in instance.CimInstanceProperties)
                if (property.Flags.HasFlag(CimFlags.Key))
                    key.CimInstanceProperties.Add(
                        CimProperty.Create(property.Name, property.Value,
                            CimFlags.Key));

            return Session.GetInstance(instance.CimSystemProperties.Namespace, key);
        }
    }
}
