using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Impersonation
{
    public class AddressMappingService : INetworkService
    {
        public required ILogger<AddressMappingService> Logger { private get; init; }

        public required IAddressCache Cache { private get; init; }

        public required NetworkDevice   Device  { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        public void Advertise(AddressMapping mapping, EthernetPacket? respondTo = null)
        {
            switch (mapping.IPAddress.AddressFamily)
            {
                case AddressFamily.InterNetwork when respondTo?.PayloadPacket is ArpPacket arp
                    && arp.Operation == ArpOperation.Request && !arp.IsProbe()
                    && arp.TargetProtocolAddress.Equals(mapping.IPAddress):
                    //Logger.LogDebug($"Received ARP request for Address {mapping.Address}");
                    SendARPResponse(mapping.IPAddress, mapping.PhysicalAddress, arp.SenderProtocolAddress, arp.SenderHardwareAddress);
                    break;

                case AddressFamily.InterNetwork:
                    SendARPAnnouncement(mapping.IPAddress, mapping.PhysicalAddress);
                    break;

                case AddressFamily.InterNetworkV6 when respondTo?.Extract<IPv6Packet>() is IPv6Packet ipv6
                    && ipv6.Extract<NdpNeighborSolicitationPacket>() is NdpNeighborSolicitationPacket ndp
                    && !ipv6.SourceAddress.Equals(IPAddress.IPv6Any) && ndp.TargetAddress.Equals(mapping.IPAddress):
                    SendNDPAdvertisement(mapping.IPAddress, mapping.PhysicalAddress, ipv6.SourceAddress, respondTo.FindSourcePhysicalAddress());
                    break;

                case AddressFamily.InterNetworkV6:
                    SendNDPAdvertisement(mapping.IPAddress, mapping.PhysicalAddress);
                    break;

                default:
                    throw new NotSupportedException($"Address family {mapping.IPAddress.AddressFamily} is not supported.");
            }
        }

        #region NetworkService lifecycle
        void INetworkService.Startup()
        {
            if (Network.Where(host => host is not LocalHost && host is not NetworkRouter) is var hosts && hosts.Any())
            {
                Logger.LogDebug("Installing static address mappings...");

                foreach (var host in hosts)
                {
                    host.AddressAdded += Host_AddressAdded;
                    host.PhysicalAddressChanged += Host_PhysicalAddressChanged;
                    host.AddressRemoved += Host_AddressRemoved;

                    if (host.PhysicalAddress is PhysicalAddress mac)
                        foreach (var ip in host.IPAddresses)
                            if (Network.LocalRange.Contains(ip))
                                Cache.Update(ip, mac);
                }
            }
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            // LATER: maybe update static mappings based on received packets?
        }

        void INetworkService.Shutdown()
        {
            if (Network.Where(host => host is not LocalHost && host is not NetworkRouter) is var hosts && hosts.Any())
            {
                Logger.LogDebug("Deleting static address mappings...");

                foreach (var host in hosts)
                {
                    host.AddressAdded -= Host_AddressAdded;
                    host.PhysicalAddressChanged -= Host_PhysicalAddressChanged;
                    host.AddressRemoved -= Host_AddressRemoved;

                    foreach (var ip in host.IPAddresses)
                        if (Network.LocalRange.Contains(ip))
                            if (host.PhysicalAddress is not null)
                                Cache.Delete(ip);
                }
            }
        }
        #endregion

        #region NetworkHost lifecycle
        private void Host_AddressAdded(object? sender, AddressEventArgs args)
        {
            if (sender is NetworkHost host && host.PhysicalAddress is PhysicalAddress mac)
            {
                Logger.LogDebug($"Updating static address mappings for host '{host.Name}'...");

                if (Network.LocalRange.Contains(args.IPAddress))
                    Cache.Update(args.IPAddress, mac);
            }
        }

        private void Host_PhysicalAddressChanged(object? sender, PhysicalAddressEventArgs args)
        {
            if (sender is NetworkHost host)
            {
                Logger.LogDebug($"Updating static address mappings for host '{host.Name}'...");

                foreach (var ip in host.IPAddresses)
                {
                    if (Network.LocalRange.Contains(ip))
                    {
                        Cache.Update(ip, args.PhysicalAddress);
                    }
                }
            }
        }

        private void Host_AddressRemoved(object? sender, AddressRemovedEventArgs args)
        {
            if (sender is NetworkHost host && host.PhysicalAddress is not null)
            {
                Logger.LogDebug($"Updating static address mappings for host '{host.Name}'...");

                if (Network.LocalRange.Contains(args.IPAddress))
                    Cache.Delete(args.IPAddress);
            }
        }
        #endregion

        #region ARP/NDP protocol implementation
        private void SendARPAnnouncement(IPAddress ip, PhysicalAddress mac, PhysicalAddress? macTarget = null)
        {
            if (macTarget == null)
            {
                Logger.LogDebug($"Sending ARP announcement <{ip} -> {mac.ToHexString()}>");

                macTarget = PhysicalAddressExt.Broadcast;
            }
            else
            {
                Logger.LogDebug($"Sending ARP announcement <{ip} -> {mac.ToHexString()}> to {macTarget.ToHexString()}");
            }

            //var response = new EthernetPacket(Address.TryParseFormat("F0-E1-D2-C3-B4-A5"), macTarget, EthernetType.Arp)
            var response = new EthernetPacket(Device.PhysicalAddress, macTarget, EthernetType.Arp)
            {
                PayloadPacket = new ArpPacket(ArpOperation.Request, PhysicalAddressExt.Empty, ip, mac, ip)
            };

            Device.SendPacket(response);
        }

        private void SendARPResponse(IPAddress ip, PhysicalAddress mac, IPAddress ipTarget, PhysicalAddress macTarget)
        {
            Logger.LogDebug($"Sending ARP response <{ip} -> {mac.ToHexString()}> to {ipTarget}");

            //var response = new EthernetPacket(Address.TryParseFormat("F0-E1-D2-C3-B4-A5"), macTarget, EthernetType.Arp)
            var response = new EthernetPacket(Device.PhysicalAddress, macTarget, EthernetType.Arp)
            {
                PayloadPacket = new ArpPacket(ArpOperation.Response, macTarget, ipTarget, mac, ip)
            };

            Device.SendPacket(response);
        }

        private void SendNDPAdvertisement(IPAddress ip, PhysicalAddress mac, IPAddress? ipTarget = null, PhysicalAddress? macTarget = null, bool unsolicited = false)
        {
            Logger.LogDebug($"Sending NDP advertisement <{ip} -> {mac.ToHexString()}>"
                + (ipTarget != null ? $" to {ipTarget}" : ""));

            NDPFlags flags = NDPFlags.Override;

            if (ipTarget != null && macTarget != null)
            {
                if (unsolicited != true)
                {
                    flags |= NDPFlags.Solicited;
                }
            }

            var ipSource = Device.IPv6LinkLocalAddress;
            ipTarget ??= IPAddressExt.LinkLocalMulticast;
            macTarget ??= ipTarget.DeriveLayer2MulticastAddress();

            var request = new EthernetPacket(Device.PhysicalAddress, macTarget, EthernetType.IPv6)
            {
                PayloadPacket = new IPv6Packet(ipSource, ipTarget).WithNDPNeighborAdvertisement(flags, ip, mac)
            };

            Device.SendPacket(request);
        }
        #endregion
    }
}
