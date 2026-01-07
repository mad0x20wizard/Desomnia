using Autofac;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class RouterAdvertismentDetector(AutoDiscoveryType auto, DiscoveryOptions options) : INetworkService, IRouterDiscovery
    {
        public required ILogger<RouterAdvertismentDetector> Logger { private get; init; }

        public required NetworkDevice   Device { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        public required NetworkContext  NetworkContext { private get; init; }

        private int _routerNr = 1;

        async Task IRouterDiscovery.DiscoverRouters(NetworkSegment network)
        {
            if (Device.IPv6LinkLocalAddress != null)
            {
                using SemaphoreSlim semaphore = new(0);

                async void Capture(object? sender, EthernetPacket packet)
                {
                    await ProcessPacketMaybeAsync(packet);

                    try { semaphore.Release(); }
                    catch (ObjectDisposedException)
                    {
                        // ignore if semaphore is already disposed
                    }
                }

                Device.EthernetCaptured += Capture;

                try
                {
                    SendNDPRouterSolicitation();

                    await semaphore.WaitAsync(options.Timeout);
                }
                finally
                {
                    Device.EthernetCaptured -= Capture;
                }
            }
        }

        #pragma warning disable CS4014 
        void INetworkService.ProcessPacket(EthernetPacket packet) => ProcessPacketMaybeAsync(packet);
        #pragma warning restore CS4014

        private async Task ProcessPacketMaybeAsync(EthernetPacket packet)
        {
            if (packet.Extract<NdpPacket>() is NdpRouterAdvertisementPacket ndp)
            {
                if (packet.FindSourcePhysicalAddress() is PhysicalAddress mac && packet.FindSourceIPAddress() is IPAddress ip)
                {
                    var lifetime = TimeSpan.FromSeconds(ndp.RouterLifetime);

                    Logger.LogDebug($"Received NDP router advertisement from {ip} -> {mac.ToHexString()} with lifetime = {lifetime}");

                    await RememberRouterAddress(mac, ip, lifetime);
                }
            }
        }

        private async Task RememberRouterAddress(PhysicalAddress mac, IPAddress ip, TimeSpan lifetime)
        {
            if (Network[mac] is not NetworkRouter router)
            {
                if (Network[ip] is NetworkRouter routerByIP)
                {
                    if (routerByIP.PhysicalAddress is null)
                    {
                        Logger.LogHostPhysicalAddressChanged(routerByIP, mac);

                        routerByIP.PhysicalAddress = mac;
                    }

                    router = routerByIP;
                }
                else if (auto.HasFlag(AutoDiscoveryType.Router))
                {
                    var config = new NetworkRouterInfo()
                    {
                        AutoDetect = auto,

                        Name = await ip.LookupName() ?? $"UnkownRouter#{_routerNr++}",

                        MAC = mac,
                        IPv6 = ip
                    };

                    var context = NetworkContext.CreateDynamicHost(new TypedParameter(typeof(NetworkRouterInfo), config));

                    router = (context.Host as NetworkRouter)!;

                    Logger.LogDebug($"Dynamically found router '{router.Name}' at {router.PhysicalAddress?.ToHexString()}");
                }
                else
                {
                    router = null!;
                }
            }

            if (router?.AddAddress(ip, lifetime) ?? false)
            {
                Logger.LogHostAddressAdded(router, ip);
            }
        }

        private void SendNDPRouterSolicitation()
        {
            if (Device.IPv6LinkLocalAddress == null)
                throw new ArgumentException($"Device '{Device.Interface.Name}' does not have a link-local IPv6 address.");

            Logger.LogDebug($"Sending NDP router solicitation");

            var ipSource = Device.IPv6LinkLocalAddress;
            var ipTarget = IPAddressExt.LinkLocalRouterMulticast;

            var request = new EthernetPacket(Device.PhysicalAddress, ipTarget.DeriveLayer2MulticastAddress(), EthernetType.IPv6)
            {
                PayloadPacket = new IPv6Packet(ipSource, ipTarget).WithNDPRouterSolicitation()
            };

            Device.SendPacket(request);
        }
    }
}
