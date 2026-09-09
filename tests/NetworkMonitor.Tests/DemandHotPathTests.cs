using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class DemandHotPathTests
    {
        private static readonly PhysicalAddress SourceMAC = PhysicalAddress.Parse("001122334455");
        private static readonly PhysicalAddress TargetMAC = PhysicalAddress.Parse("AABBCCDDEEFF");
        private static readonly IPAddress SourceIP = IPAddress.Parse("192.0.2.10");
        private static readonly IPAddress TargetIP = IPAddress.Parse("192.0.2.20");

        private sealed class TestHostWatch : NetworkHostWatch { }

        private sealed class TestDemandWatch : HostDemandWatch
        {
            public override bool IsOnline => true;
        }

        private static NetworkSegment CreateNetwork() => new()
        {
            Logger = NullLogger<NetworkSegment>.Instance,
            Device = null!,
            LocalRange = null!,
            Ranges = null!
        };

        private static EthernetPacket CreateTCPPacket(bool synchronize, byte[] payload)
        {
            var tcp = new TcpPacket(50000, 445)
            {
                Synchronize = synchronize,
                PayloadData = payload
            };

            return new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv4)
            {
                PayloadPacket = new IPv4Packet(SourceIP, TargetIP)
                {
                    PayloadPacket = tcp
                }
            };
        }

        [Fact]
        public void PacketSummary_ExtractsTransportMetadataAndPayloadLengthOnce()
        {
            byte[] payload = [1, 2, 3, 4, 5];

            var summary = new CaptureSummary(CreateTCPPacket(synchronize: false, payload));

            Assert.Equal(SourceIP, summary.SourceAddress);
            Assert.Equal(TargetIP, summary.TargetAddress);
            Assert.Equal(new IPPort(IPProtocol.TCP, 50000), summary.SourceService);
            Assert.Equal(new IPPort(IPProtocol.TCP, 445), summary.TargetService);
            Assert.Equal(payload.Length, summary.Extract<TransportPacket>()?.PayloadLength);
            Assert.False(summary.Extract<TcpPacket>()!.Synchronize);
        }

        [Fact]
        public void PacketSummary_CountsDecodedTransportPayloadWithoutPayloadDataCopy()
        {
            var wakeOnLan = new WakeOnLanPacket(TargetMAC);
            var udp = new UdpPacket(50000, 9) { PayloadPacket = wakeOnLan };
            var ip = new IPv4Packet(SourceIP, TargetIP)
            {
                Protocol = ProtocolType.Udp,
                PayloadPacket = udp
            };
            var packet = new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv4)
            {
                PayloadPacket = ip
            };

            var summary = new CaptureSummary(packet);

            Assert.Equal(wakeOnLan.TotalPacketLength, summary.Extract<TransportPacket>()?.PayloadLength);
            Assert.True(summary.Ethernet.IsMagicPacket());
        }

        [Fact]
        public void NetworkSegment_IndexesFollowAddressChanges()
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target")
            {
                Network = network,
                PhysicalAddress = TargetMAC
            };

            host.AddAddress(TargetIP);
            network.AddHost(host);

            Assert.Same(host, network[TargetIP]);
            Assert.Same(host, network[TargetMAC]);

            var replacementMAC = PhysicalAddress.Parse("AABBCCDDEE00");
            host.PhysicalAddress = replacementMAC;
            host.RemoveAddress(TargetIP);

            Assert.Null(network[TargetIP]);
            Assert.Null(network[TargetMAC]);
            Assert.Same(host, network[replacementMAC]);

            network.RemoveHost(host);

            Assert.Null(network[replacementMAC]);
        }

        [Fact]
        public void MonitorIndexesFollowHostAndServiceTracking()
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target") { Network = network };
            var hostWatch = new TestHostWatch
            {
                Host = host,
                Logger = NullLogger<NetworkHostWatch>.Instance
            };
            var serviceWatch = new NetworkServiceWatch(
                new TransportNetworkService("SMB", new(IPProtocol.TCP, 445)));
            var monitor = new NetworkMonitor
            {
                Logger = NullLogger<NetworkMonitor>.Instance,
                Name = "test",
                Options = default,
                Device = null!,
                Network = network,
                Janitor = null!
            };

            monitor.StartTracking(hostWatch, adopt: false);
            hostWatch.StartTracking(serviceWatch, adopt: false);

            Assert.Same(hostWatch, monitor[host]);
            Assert.Same(serviceWatch, hostWatch.FirstOrDefault(watch => watch.Service is TransportNetworkService trans && trans.Serves(new(IPProtocol.TCP, 445))));
            Assert.Same(serviceWatch, hostWatch[serviceWatch.Service]);

            hostWatch.StopTracking(serviceWatch);
            monitor.StopTracking(hostWatch);

            Assert.Null(monitor[host]);
            Assert.Null(hostWatch.FirstOrDefault(watch => watch.Service is TransportNetworkService trans && trans.Serves(new(IPProtocol.TCP, 445))));
            Assert.Null(hostWatch[serviceWatch.Service]);
        }

        [Fact]
        public void EstablishedTCP_BypassesDemandFilterAndIsStillAccounted()
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target") { Network = network };
            bool filterWasResolved = false;

            var watch = new TestDemandWatch
            {
                Host = host,
                Logger = NullLogger<NetworkHostWatch>.Instance,
                Scope = null!,
                Device = null!,
                Network = network,
                AdvertiseOptions = default,
                HandoffOptions = default,
                DemandOptions = new DemandOptions { Source = DemandSource.IP, Parallel = 1 },
                Filter = new Lazy<IPacketFilter>(() =>
                {
                    filterWasResolved = true;
                    throw new Xunit.Sdk.XunitException("established TCP must not resolve the demand filter");
                }),
                DefaultFilterOptions = default
            };

            var summary = new CaptureSummary(CreateTCPPacket(synchronize: false, [1, 2, 3, 4]));

            Assert.Null(watch.Evaluate(summary));
            Assert.False(filterWasResolved);

            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(TimeSpan.FromSeconds(1))));
            Assert.Equal(4, usage.Bytes);
        }
    }
}
