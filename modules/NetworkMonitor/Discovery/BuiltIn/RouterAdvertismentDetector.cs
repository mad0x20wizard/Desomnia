using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class RouterAdvertismentDetector(AutoDiscoveryType auto, DiscoveryOptions options) : INetworkService, IRouterDiscovery
    {
        public required ILogger<RouterAdvertismentDetector> Logger { private get; init; }

        public required NetworkDevice   Device { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        private int _routerNr = 1;

        volatile ChannelWriter<RouterAdvertisement>? _advertisements;

        async IAsyncEnumerable<NetworkRouterContext> IRouterDiscovery.DiscoverRouters(NetworkContext ctx)
        {
            if (!auto.HasFlag(AutoDiscoveryType.Router) || Device.IPv6LinkLocalAddress is null)
                yield break;

            Dictionary<PhysicalAddress, DiscoveredRouterInfo> routers = [];
            using (LocalChannel<RouterAdvertisement> channel = Channel.CreateUnbounded<RouterAdvertisement>(new() { SingleReader = true }))
            {
                // Discovery passes are serialized; packet processing only reads this writer.
                _advertisements = channel.Writer;

                try
                {
                    using var timeout = new CancellationTokenSource(options.Timeout);
                    using var completion = timeout.Token.Register(() => channel.Writer.TryComplete());

                    SendNDPRouterSolicitation();

                    await foreach (var advertisement in channel.Reader.ReadAllAsync())
                    {
                        if (!routers.TryGetValue(advertisement.PhysicalAddress, out var router))
                        {
                            routers.Add(advertisement.PhysicalAddress, router = new()
                            {
                                AutoDetect = auto,
                                Name = $"UnkownRouter#{_routerNr++}",
                                MAC = advertisement.PhysicalAddress,
                            });
                        }

                        router.Addresses.Add(advertisement.IPAddress);
                        router.ValidUntil = advertisement.ValidUntil;
                    }
                }
                finally
                {
                    _advertisements = null;
                }
            }

            foreach (var info in routers.Values)
            {
                NetworkRouterContext? context;

                try
                {
                    info.Name = await info.Addresses.First().LookupName() ?? info.Name;

                    // Lookup may take time. Do not recreate a known router or extend an expired advertisement.
                    if (IsKnownRouter(info) || info.ValidUntil <= DateTime.Now)
                        continue;

                    context = ctx.CreateDynamicRouter<NetworkRouterContext>(info, info.ValidUntil);

                    Logger.LogDebug($"Dynamically found router '{context.Host.Name}' at {context.Host.PhysicalAddress?.ToHexString()}");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to create router from advertisement at {Address}", info.Addresses.First());

                    continue;
                }

                yield return context;
            }
        }

        private bool IsKnownRouter(DiscoveredRouterInfo info) => Network[info.MAC] is NetworkRouter || info.Addresses.Any(ip => Network[ip] is NetworkRouter);

        void INetworkService.ProcessCapture(INetworkCapture packet)
        {
            if (packet.FindPacket<NdpPacket>() is NdpRouterAdvertisementPacket ndp)
            {
                if (packet.SourcePhysicalAddress is PhysicalAddress mac && packet.SourceAddress is IPAddress ip)
                {
                    var lifetime = TimeSpan.FromSeconds(ndp.RouterLifetime);

                    if ((Network[mac] as NetworkRouter ?? Network[ip] as NetworkRouter) is NetworkRouter router)
                    {
                        RememberRouterAddress(router, mac, ip, lifetime);
                    }
                    else
                    {
                        _advertisements?.TryWrite(new(mac, ip, lifetime));
                    }
                }
            }
        }

        private void RememberRouterAddress(NetworkRouter router, PhysicalAddress mac, IPAddress ip, TimeSpan lifetime)
        {
            if (router.PhysicalAddress is null)
            {
                Logger.LogHostPhysicalAddressChanged(router, mac);

                router.PhysicalAddress = mac;
            }

            if (router.AddAddress(ip, new(lifetime)))
            {
                Logger.LogHostAddressAdded(router, ip);
            }

            router.ValidUntil = DateTime.Now + lifetime;
        }

        private void SendNDPRouterSolicitation()
        {
            if (Device.IPv6LinkLocalAddress == null)
                throw new ArgumentException($"Device '{Device.Interface.Name}' does not have a link-local IPv6 address.");

            Logger.LogDebug($"Sending NDP router solicitation");

            var ipSource = Device.IPv6LinkLocalAddress;
            var ipTarget = IPAddress.LinkLocalRouterMulticast;

            var request = new EthernetPacket(Device.PhysicalAddress, ipTarget.DeriveLayer2MulticastAddress(), EthernetType.IPv6)
            {
                PayloadPacket = new IPv6Packet(ipSource, ipTarget).WithNDPRouterSolicitation()
            };

            Device.SendPacket(request);
        }

        private sealed class DiscoveredRouterInfo : NetworkRouterInfo
        {
            internal HashSet<IPAddress> Addresses { get; } = [];
            internal DateTime ValidUntil { get; set; }

            public override IEnumerable<IPAddress> IPAddresses => Addresses;
        }
    }
}
