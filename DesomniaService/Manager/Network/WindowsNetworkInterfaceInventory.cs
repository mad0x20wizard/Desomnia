using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    internal static class WindowsNetworkInterfaceInventory
    {
        // Retain rich .NET snapshots for IP-bound adapters. NDIS filter modules that
        // only appear in .NET's IncludeAllInterfaces inventory must not become watches.
        // CIM adds genuine adapters omitted from that set, notably disabled adapters.
        internal static IEnumerable<NetworkInterface> Merge(IEnumerable<NetworkInterface> interfaces,
            IReadOnlySet<Guid> ipInterfaceIds, IEnumerable<NetworkInterface> adapters)
        {
            Dictionary<Guid, NetworkInterface> snapshots = [];
            HashSet<Guid> seen = [];
            foreach (var nic in interfaces)
            {
                if (!Guid.TryParse(nic.Id, out var id)) continue;
                snapshots.TryAdd(id, nic);
                if (ipInterfaceIds.Contains(id) && seen.Add(id))
                    yield return nic;
            }
            foreach (var adapter in adapters)
            {
                if (Guid.TryParse(adapter.Id, out var id) && seen.Add(id))
                    yield return snapshots.GetValueOrDefault(id) ?? adapter;
            }
        }
    }
}
