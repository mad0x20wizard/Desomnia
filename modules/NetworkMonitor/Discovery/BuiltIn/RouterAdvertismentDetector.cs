using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Extensions;
using MadWizard.Desomnia.Network.Neighborhood;
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

        private SemaphoreSlim? _semaphore;

        private bool _discoverPassively = false;

        async Task IRouterDiscovery.DiscoverRouters(NetworkContext ctx)
        {
            if (Device.IPv6LinkLocalAddress != null)
            {
                _semaphore = new(0);

                try
                {
                    SendNDPRouterSolicitation();

                    await _semaphore.WaitAsync(options.Timeout);
                }
                finally
                {
                    _discoverPassively = true;

                    _semaphore = null;
                }
            }
        }

        async void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            if (_semaphore != null || _discoverPassively)
            {
                if (packet.Extract<NdpPacket>() is NdpRouterAdvertisementPacket ndp)
                {
                    if (packet.FindSourcePhysicalAddress() is PhysicalAddress mac && packet.FindSourceIPAddress() is IPAddress ip)
                    {
                        var lifetime = TimeSpan.FromSeconds(ndp.RouterLifetime);

                        //Logger.LogTrace($"Received NDP router advertisement from {ip} -> {mac.ToHexString()} with lifetime = {lifetime}");

                        await RememberRouterAddress(mac, ip, lifetime);

                        _semaphore?.ReleaseFinally();
                    }
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

                    var context = await NetworkContext.CreateDynamicRouter<NetworkRouterContext>(config);

                    router = (NetworkRouter)context.Host;

                    Logger.LogDebug($"Dynamically found router '{router.Name}' at {router.PhysicalAddress?.ToHexString()}");
                }
                else
                {
                    router = null!;
                }
            }

            if (router is not null)
            {
                router.ValidUntil = DateTime.Now + lifetime;

                if (router.AddAddress(ip, new(lifetime)))
                {
                    Logger.LogHostAddressAdded(router, ip);
                }
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
