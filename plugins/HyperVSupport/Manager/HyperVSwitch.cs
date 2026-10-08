using MadWizard.Desomnia.Network.Manager;
using Microsoft.Management.Infrastructure;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal class HyperVSwitch(HyperVManager manager, Guid guid, string name)
    {
        public Guid GUID { get; } = guid;
        public string Name { get; } = name;

        public IReadOnlyList<NetworkIdentity> Interfaces => field ??= Ports
            .Where(port => port.Type == HyperVSwitchType.Internal)
            .Select(port => port.Identity).Distinct().ToArray();

        public HyperVSwitchType Type => Ports.Any(port => port.Type == HyperVSwitchType.External)
            ? HyperVSwitchType.External
            : Interfaces.Count > 0 ? HyperVSwitchType.Internal : HyperVSwitchType.Private;

        // A switch is a snapshot for one device selection, not a permanent topology cache.
        private IReadOnlyList<Port> Ports => field ??= QueryPorts().Distinct().ToArray();

        internal (NetworkIdentity Identity, string Name)? QueryPhysicalInterface()
        {
            var ports = Ports.Where(port => port.Type == HyperVSwitchType.External).ToArray();

            if (ports.Length > 1)
                throw new NotSupportedException($"Hyper-V switch '{Name}' has multiple physical interfaces; a single capture device cannot monitor all uplinks.");

            return ports.Length == 1 ? (ports[0].Identity, ports[0].Name) : null;
        }

        #region CIM Queries
        internal IEnumerable<PhysicalAddress> QueryVirtualMachineAddresses()
        {
            // Query current configuration, not live switch ports: powered-off VMs
            // still need to be reachable. Exclude checkpoint and planned settings.
            const string QUERY = "SELECT * FROM Msvm_VirtualSystemSettingData " +
                "WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'";
            var session = manager.Session;

            foreach (var settings in session.QueryInstances(HyperVManager.NS, HyperVManager.DIALECT, QUERY))
            {
                using (settings)
                foreach (var adapter in session.EnumerateAssociatedInstances(HyperVManager.NS, settings,
                    "CIM_ResourceAllocationSettingData", "Msvm_VirtualSystemSettingDataComponent"))
                {
                    using (adapter)
                    {
                        if (adapter.CimSystemProperties.ClassName is not
                            ("Msvm_SyntheticEthernetPortSettingData" or "Msvm_EmulatedEthernetPortSettingData"))
                            continue;

                        foreach (var connection in session.EnumerateAssociatedInstances(HyperVManager.NS, adapter,
                            "Msvm_EthernetPortAllocationSettingData", "Msvm_ResourceDependentOnResource"))
                        {
                            using (connection)
                            {
                                var resources = connection.CimInstanceProperties["HostResource"]?.Value as string[];
                                if (resources?.Any(resource => GetSwitchId(resource) == GUID) != true)
                                    continue;

                                var value = adapter.CimInstanceProperties["Address"]?.Value as string;
                                // A dynamic MAC may not have been assigned to a new VM yet.
                                if (string.IsNullOrEmpty(value) || value == "000000000000")
                                    continue;
                                if (!PhysicalAddress.TryParse(value, out var address) || address.GetAddressBytes().Length != 6)
                                    throw new InvalidOperationException($"Invalid MAC address '{value}' on Hyper-V switch '{Name}'.");
                                yield return address;
                            }
                        }
                    }
                }
            }
        }

        internal static Guid? GetSwitchId(string resource)
        {
            // HostResource is a CIM object path, not a switch display name. Read the
            // exact Name key of a switch reference; both key orders are valid.
            var match = Regex.Match(resource,
                """(?:^|:)Msvm_VirtualEthernetSwitch\.(?:CreationClassName="Msvm_VirtualEthernetSwitch",\s*)?Name="(?<id>[^"]+)"(?:,\s*CreationClassName="Msvm_VirtualEthernetSwitch")?$""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id) ? id : null;
        }

        internal CimInstance QueryInstance()
        {
            var QUERY = $"SELECT * FROM Msvm_VirtualEthernetSwitch WHERE Name = '{GUID:D}'";

            return manager.Session.QueryInstances(HyperVManager.NS, HyperVManager.DIALECT, QUERY).FirstOrDefault()
                ?? throw new InvalidOperationException($"Hyper-V switch '{GUID}' not found.");
        }

        private IEnumerable<Port> QueryPorts()
        {
            using var instance = QueryInstance();
            var session = manager.Session;

            // Switch port -> switch endpoint -> connected endpoint -> host/physical adapter.
            foreach (var switchPort in session.EnumerateAssociatedInstances(HyperVManager.NS, instance,
                "Msvm_EthernetSwitchPort", "Msvm_SystemDevice"))
            {
                using (switchPort)
                foreach (var endpoint in session.EnumerateAssociatedInstances(HyperVManager.NS, switchPort,
                    "CIM_ProtocolEndpoint", "CIM_DeviceSAPImplementation"))
                {
                    using (endpoint)
                    foreach (var peer in session.EnumerateAssociatedInstances(HyperVManager.NS, endpoint,
                        "CIM_ProtocolEndpoint", "Msvm_ActiveConnection"))
                    {
                        using (peer)
                        foreach (var adapter in session.EnumerateAssociatedInstances(HyperVManager.NS, peer,
                            "CIM_NetworkPort", "CIM_DeviceSAPImplementation"))
                        {
                            using (adapter)
                            {
                                HyperVSwitchType? type = adapter.CimSystemProperties.ClassName switch
                                {
                                    "Msvm_InternalEthernetPort" => HyperVSwitchType.Internal,
                                    "Msvm_ExternalEthernetPort" or "Msvm_WiFiPort" => HyperVSwitchType.External,
                                    _ => null
                                };

                                if (type is null)
                                    continue;

                                var deviceId = adapter.CimInstanceProperties["DeviceID"]?.Value as string;
                                var identity = GetNetworkIdentity(deviceId)
                                    ?? throw new InvalidOperationException($"Cannot identify adapter '{deviceId}' of Hyper-V switch '{Name}'.");

                                yield return new Port(identity,
                                    adapter.CimInstanceProperties["ElementName"]?.Value as string ?? identity.Id, type.Value);
                            }
                        }
                    }
                }
            }
        }

        internal static NetworkIdentity? GetNetworkIdentity(string? deviceId)
        {
            // Hyper-V's adapter DeviceID is Microsoft:{interface GUID}. The adapter's
            // Name and the switch's Name identify different objects.
            const string PREFIX = "Microsoft:";
            if (deviceId?.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase) == true)
                deviceId = deviceId[PREFIX.Length..];

            return Guid.TryParse(deviceId, out var id)
                ? new NetworkIdentity(id.ToString("B").ToUpperInvariant()) : null;
        }
        #endregion

        private readonly record struct Port(NetworkIdentity Identity, string Name, HyperVSwitchType Type);

        public override string ToString() => $"HyperVSwitch[name='{Name}', guid={GUID}, type={Type}]";
    }
}
