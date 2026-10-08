using MadWizard.Desomnia.Network.Interface.Manager;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Windows interface inventory and administrative operations through MSFT_NetAdapter.
    /// IP Helper supplies state for interfaces that have no corresponding CIM adapter.
    /// CIM supplements the .NET inventory with disabled adapters, including ones disabled
    /// before Desomnia started. Administrative state is independent of link connectivity.
    /// </summary>
    internal sealed class CIMNetworkInterfaceManager : NetworkInterfaceManager
    {
        private const string AdapterNamespace = @"root\StandardCimv2";

        protected override void DisableInterface(INetworkInterface @interface)
        {
            InvokeOnAdapter(@interface.Identity.Id, "Disable");
        }

        protected override void EnableInterface(INetworkInterface @interface)
        {
            InvokeOnAdapter(@interface.Identity.Id, "Enable");
        }

        protected override IEnumerable<NetworkInterface> QueryInterfaces()
        {
            var visible = base.QueryInterfaces().ToArray();
            var identities = visible.Select(n => n.ToIdentity()).ToHashSet();
            foreach (var nic in visible) yield return nic;

            // .NET omits some disabled Windows adapters. Include them in the physical
            // inventory so disabled="false" also works on the first application run.
            using var session = CimSession.Create(null);
            foreach (var adapter in EnumerateAdapters(session))
            {
                using (adapter)
                {
                    if (adapter.CimInstanceProperties["InterfaceGuid"]?.Value is not string id
                        || !Guid.TryParse(id, out var guid)) continue;
                    var identity = new NetworkIdentity(guid.ToString("B").ToUpperInvariant());
                    if (identities.Contains(identity)) continue;
                    var status = (OperationalStatus)Convert.ToInt32(adapter.CimInstanceProperties["InterfaceOperationalStatus"]?.Value ?? 6);
                    if (status == OperationalStatus.NotPresent) continue;
                    yield return new AdapterSnapshot(identity.Id,
                        adapter.CimInstanceProperties["Name"]?.Value as string ?? id,
                        (NetworkInterfaceType)Convert.ToInt32(adapter.CimInstanceProperties["InterfaceType"]?.Value ?? 1), status);
                }
            }
        }

        protected override bool IsInterfaceDisabled(INetworkInterface @interface)
        {
            // The IP interface inventory includes hidden virtual and filter interfaces that
            // MSFT_NetAdapter does not necessarily expose. A missing CIM row is not removal.
            if (Guid.TryParse(@interface.Identity.Id, out var id)
                && NetworkInterfaceInterop.TryGetDisabled(id, out bool disabled)) return disabled;

            // Administratively disabled adapters may be absent from the IP interface table.
            using var session = CimSession.Create(null);
            using var adapter = FindAdapter(session, @interface.Identity.Id)
                ?? throw new InvalidOperationException($"Interface '{@interface.Name}' is no longer present.");
            var value = adapter.CimInstanceProperties["InterfaceAdminStatus"]?.Value
                ?? throw new InvalidOperationException($"Cannot read administrative state of '{@interface.Name}'.");
            return Convert.ToInt32(value) != 1;
        }

        private sealed class AdapterSnapshot(string id, string name, NetworkInterfaceType type, OperationalStatus status) : NetworkInterface
        {
            public override string Id => id;
            public override string Name => name;
            public override string Description => name;
            public override NetworkInterfaceType NetworkInterfaceType => type;
            public override OperationalStatus OperationalStatus => status;
            public override PhysicalAddress GetPhysicalAddress() => PhysicalAddress.None;
            public override IPInterfaceProperties GetIPProperties() => throw new NetworkInformationException();
            public override bool Supports(NetworkInterfaceComponent component) => false;
        }
        protected override string? GetSSID(INetworkInterface @interface)
        {
            // only a wireless adapter can be joined to anything - checking the type first spares
            // the WLAN service a round trip for every other interface on the machine
            if (@interface.Type != NetworkInterfaceType.Wireless80211)
                return null;

            if (!Guid.TryParse(@interface.Identity.Id, out Guid interfaceId))
                return null;

            return WiFiInterop.GetCurrentSSID(interfaceId);
        }

        private static void InvokeOnAdapter(string interfaceId, string methodName)
        {
            using var session = CimSession.Create(null);

            using var adapter = FindAdapter(session, interfaceId)
                ?? throw new InvalidOperationException($"No network adapter with interface id '{interfaceId}' found.");

            using var result = session.InvokeMethod(AdapterNamespace, adapter, methodName, null);
            uint code = Convert.ToUInt32(result?.ReturnValue?.Value ?? throw new InvalidOperationException("CIM returned no result."));
            if (code != 0 && code != 4096)
                throw new InvalidOperationException($"{methodName} failed for '{interfaceId}' (CIM {code}).");
        }

        private static CimInstance? FindAdapter(CimSession session, string interfaceId)
        {
            string guid = interfaceId.Trim('{', '}');

            foreach (var instance in EnumerateAdapters(session))
            {
                if (instance.CimInstanceProperties["InterfaceGuid"]?.Value is string interfaceGuid
                    && string.Equals(interfaceGuid.Trim('{', '}'), guid, StringComparison.OrdinalIgnoreCase))
                {
                    return instance;
                }

                instance.Dispose();
            }

            return null;
        }

        private static IEnumerable<CimInstance> EnumerateAdapters(CimSession session)
        {
            using var options = new CimOperationOptions();
            options.SetCustomOption("IncludeHidden", true, false);
            foreach (var adapter in session.EnumerateInstances(AdapterNamespace, "MSFT_NetAdapter", options))
                yield return adapter;
        }
    }
}
