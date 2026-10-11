using MadWizard.Desomnia.Network.Manager;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class WindowsNetworkInterfaceInventoryTests
{
    [Fact]
    public void NdisFilterModulesDoNotEnterTheManagedInventory()
    {
        var ethernet = new Adapter("Ethernet");
        var filters = new[]
        {
            new Adapter("WLAN-WFP Native MAC Layer LightWeight Filter-0000"),
            new Adapter("WLAN-Npcap Packet Driver (NPCAP)-0000"),
            new Adapter("WLAN-QoS Packet Scheduler-0000")
        };

        var result = WindowsNetworkInterfaceInventory.Merge([ethernet, .. filters],
            new HashSet<Guid> { ethernet.Guid }, [ethernet]).ToArray();

        Assert.Same(ethernet, Assert.Single(result));
    }

    [Theory]
    [InlineData("Loopback Pseudo-Interface 1", NetworkInterfaceType.Loopback)]
    [InlineData("Hidden VPN", NetworkInterfaceType.Tunnel)]
    [InlineData("vEthernet (Default Switch)", NetworkInterfaceType.Ethernet)]
    public void IpBoundVirtualInterfacesRemainWithoutCimRows(string name, NetworkInterfaceType type)
    {
        var nic = new Adapter(name, type: type);
        var result = WindowsNetworkInterfaceInventory.Merge([nic], new HashSet<Guid> { nic.Guid }, []);
        Assert.Same(nic, Assert.Single(result));
    }

    [Fact]
    public void DisabledCimOnlyAdapterRemainsAvailableForReenabling()
    {
        var disabled = new Adapter("Disabled Ethernet", status: OperationalStatus.Down);
        var result = WindowsNetworkInterfaceInventory.Merge([], new HashSet<Guid>(), [disabled]);
        Assert.Same(disabled, Assert.Single(result));
    }

    [Fact]
    public void CimRecognizedAdapterWithoutIpBindingsRetainsItsRichSnapshot()
    {
        var nic = new Adapter("Unbound Ethernet");
        var cim = new Adapter("CIM fallback", nic.Guid);
        var result = WindowsNetworkInterfaceInventory.Merge([nic], new HashSet<Guid>(), [cim]);
        Assert.Same(nic, Assert.Single(result));
    }

    [Fact]
    public void DifferentGuidFormattingDoesNotDuplicateAdaptersOrReplaceIpMetadata()
    {
        var nic = new Adapter("Ethernet");
        var cim = new Adapter("CIM fallback", nic.Guid, braced: false);
        var result = WindowsNetworkInterfaceInventory.Merge([nic], new HashSet<Guid> { nic.Guid }, [cim]);
        Assert.Same(nic, Assert.Single(result));
    }

    private sealed class Adapter(string name, Guid? id = null,
        NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
        OperationalStatus status = OperationalStatus.Up, bool braced = true) : NetworkInterface
    {
        public Guid Guid { get; } = id ?? System.Guid.NewGuid();
        public override string Id => braced ? Guid.ToString("B").ToUpperInvariant() : Guid.ToString("D").ToLowerInvariant();
        public override string Name => name;
        public override NetworkInterfaceType NetworkInterfaceType => type;
        public override OperationalStatus OperationalStatus => status;
    }
}
