using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class NetworkServiceHostRangeTests
    {
        private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");
        private static readonly IPAddress Target = IPAddress.Parse("192.0.2.20");

        [Theory]
        [InlineData(PacketDirection.Inbound)]
        [InlineData(PacketDirection.Outbound)]
        public void RegisteredRangeAppliesOnlyBaseServiceRules(PacketDirection direction)
        {
            var info = new DynamicHostRangeInfo { Name = "services" };
            info.ServiceFilterRule.Add(new ServiceFilterRuleInfo
            {
                Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 443, Type = FilterRuleType.Must,
                HostFilterRule = [new() { Name = "unresolvable", Type = FilterRuleType.Must }],
                HostRangeFilterRule = [new() { Name = "unresolvable", Type = FilterRuleType.Must }]
            });
            info.ServiceFilterRule.Add(new HTTPFilterRuleInfo
            {
                Port = 80, Type = FilterRuleType.Must,
                RequestFilterRule = [new() { Path = "/restricted", Type = FilterRuleType.Must }]
            });
            info.ServiceFilterRule.Add(new ServiceFilterRuleInfo
            {
                Protocol = IPProtocol.UDP, Port = 443, Type = FilterRuleType.MustNot
            });
            using var container = Register(info);
            var range = Assert.IsType<NetworkServiceHostRange>(container.ResolveNamed<NetworkHostRange>(info.Name));
            range.AddAddress(Peer);
            var rule = new DynamicHostRangeFilterRule { Range = range, Type = FilterRuleType.Must };

            Assert.True(rule.Matches(Packet(direction, IPProtocol.TCP, 443), direction));
            Assert.False(rule.Matches(Packet(direction, IPProtocol.UDP, 443), direction)); // veto wins
            Assert.False(rule.Matches(Packet(direction, IPProtocol.TCP, 444), direction));
            Assert.True(rule.Matches(Packet(direction, IPProtocol.TCP, 80), direction)); // no HTTP payload needed
            range.RemoveAddress(Peer);
            Assert.False(rule.Matches(Packet(direction, IPProtocol.TCP, 443), direction));
            Assert.All(range.Rules, service => Assert.Empty(service.HostRules));
        }

        [Theory]
        [InlineData(IPProtocol.TCP)]
        [InlineData(IPProtocol.UDP)]
        public void CombinedProtocolAllowsBothTransports(IPProtocol protocol)
        {
            var info = new DynamicHostRangeInfo { Name = "combined" };
            info.ServiceFilterRule.Add(new() { Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 53, Type = FilterRuleType.Must });
            using var container = Register(info);
            var range = container.ResolveNamed<NetworkHostRange>(info.Name);
            range.AddAddress(Peer);
            var rule = new DynamicHostRangeFilterRule { Range = range, Type = FilterRuleType.Must };
            Assert.True(rule.Matches(Packet(PacketDirection.Inbound, protocol, 53)));
            Assert.False(rule.Matches(Packet(PacketDirection.Inbound, protocol, 54)));
        }

        [Fact]
        public void EmptyServiceRulesDoNotInheritContainerRules()
        {
            var info = new DynamicHostRangeInfo { Name = "unrestricted" };
            using var container = Register(info);
            var range = Assert.IsType<NetworkServiceHostRange>(container.ResolveNamed<NetworkHostRange>(info.Name));
            Assert.Empty(range.Rules);
            range.AddAddress(Peer);
            var rule = new DynamicHostRangeFilterRule { Range = range, Type = FilterRuleType.Must };
            Assert.True(rule.Matches(Packet(PacketDirection.Inbound, IPProtocol.UDP, 123)));
        }

        private static IContainer Register(DynamicHostRangeInfo info)
        {
            var config = new NetworkMonitorConfig();
            config.DynamicHostRange.Add(info);
            var builder = new ContainerBuilder();
            builder.Register<TransportFilterRule>(_ => throw new InvalidOperationException("Must not resolve container service rules"));
            var context = (NetworkContext)RuntimeHelpers.GetUninitializedObject(typeof(NetworkContext));
            typeof(NetworkContext).GetMethod("RegisterHostRanges", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(context, [builder, config]);
            return builder.Build();
        }

        private static EthernetPacket Packet(PacketDirection direction, IPProtocol protocol, ushort port)
        {
            bool inbound = direction == PacketDirection.Inbound;
            ushort source = inbound ? (ushort)50000 : port;
            ushort destination = inbound ? port : (ushort)50000;
            TransportPacket transport = protocol == IPProtocol.TCP ? new TcpPacket(source, destination) : new UdpPacket(source, destination);
            return new EthernetPacket(PhysicalAddress.Parse("001122334455"), PhysicalAddress.Parse("AABBCCDDEEFF"), EthernetType.IPv4)
            {
                PayloadPacket = new IPv4Packet(inbound ? Peer : Target, inbound ? Target : Peer) { PayloadPacket = transport }
            };
        }
    }
}
