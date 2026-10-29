using MadWizard.Desomnia.Network.Capture;
using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Filter;
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
        [InlineData(FlowDirection.Inbound)]
        [InlineData(FlowDirection.Outbound)]
        public void RegisteredRangeAppliesOnlyBaseServiceRules(FlowDirection direction)
        {
            var info = new DynamicHostRangeInfo { Name = "services" };
            info.ServiceFilterRule.Add(new ServiceFilterRuleInfo
            {
                Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 443, Type = FilterRuleType.May,
                HostFilterRule = [new() { Name = "unresolvable", Type = FilterRuleType.Must }],
                HostRangeFilterRule = [new() { Name = "unresolvable", Type = FilterRuleType.Must }]
            });
            info.ServiceFilterRule.Add(new ServiceFilterRuleInfo
            {
                Port = 80, Type = FilterRuleType.May
            });
            info.ServiceFilterRule.Add(new ServiceFilterRuleInfo
            {
                Protocol = IPProtocol.UDP, Port = 443, Type = FilterRuleType.MustNot
            });
            using var container = Register(info);
            var range = Assert.IsType<NetworkServiceHostRange>(container.ResolveNamed<NetworkHostRange>(info.Name));
            range.AddAddress(Peer);
            var rule = new DynamicHostRangeFilterRule { Range = range, Type = FilterRuleType.Must };

            Assert.True(rule.Matches(Context(direction, IPProtocol.TCP, 443)));
            Assert.False(rule.Matches(Context(direction, IPProtocol.UDP, 443))); // veto wins
            Assert.False(rule.Matches(Context(direction, IPProtocol.TCP, 444)));
            Assert.True(rule.Matches(Context(direction, IPProtocol.TCP, 80))); // no HTTP payload needed
            range.RemoveAddress(Peer);
            Assert.False(rule.Matches(Context(direction, IPProtocol.TCP, 443)));
            Assert.All(range.Rules, service => Assert.Empty(service.Rules));
        }

        [Theory]
        [InlineData(IPProtocol.TCP)]
        [InlineData(IPProtocol.UDP)]
        public void CombinedProtocolAllowsBothTransports(IPProtocol protocol)
        {
            var info = new DynamicHostRangeInfo { Name = "combined" };
            info.ServiceFilterRule.Add(new() { Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 53, Type = FilterRuleType.May });
            using var container = Register(info);
            var range = container.ResolveNamed<NetworkHostRange>(info.Name);
            range.AddAddress(Peer);
            var rule = new DynamicHostRangeFilterRule { Range = range, Type = FilterRuleType.Must };
            Assert.True(rule.Matches(Context(FlowDirection.Inbound, protocol, 53)));
            Assert.False(rule.Matches(Context(FlowDirection.Inbound, protocol, 54)));
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
            Assert.True(rule.Matches(Context(FlowDirection.Inbound, IPProtocol.UDP, 123)));
            Assert.False(rule.Matches(new CaptureFilterContext(Peer) { Direction = FlowDirection.Inbound }));
        }

        [Fact]
        public void CombinedMandatoryProtocolsAreRejectedForDynamicRangesToo()
        {
            var info = new DynamicHostRangeInfo { Name = "invalid" };
            info.ServiceFilterRule.Add(new() { Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 53, Type = FilterRuleType.Must });
            var exception = Assert.Throws<TargetInvocationException>(() => Register(info));
            Assert.IsType<FormatException>(exception.InnerException);
        }

        private static IContainer Register(DynamicHostRangeInfo info)
        {
            var config = new NetworkMonitorConfig();
            config.DynamicHostRange.Add(info);
            var builder = new ContainerBuilder();
            builder.Register<ServiceFilterRule>(_ => throw new InvalidOperationException("Must not resolve container service rules"));
            var context = (NetworkContext)RuntimeHelpers.GetUninitializedObject(typeof(NetworkContext));
            typeof(NetworkContext).GetMethod("RegisterHostRanges", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(context, [builder, config]);
            return builder.Build();
        }

        private static IFilterContext Context(FlowDirection direction, IPProtocol protocol, ushort port)
        {
            bool inbound = direction == FlowDirection.Inbound;
            ushort source = inbound ? (ushort)50000 : port;
            ushort destination = inbound ? port : (ushort)50000;
            TransportPacket transport = protocol == IPProtocol.TCP ? new TcpPacket(source, destination) : new UdpPacket(source, destination);
            var capture = new EthernetCapture(new EthernetPacket(PhysicalAddress.Parse("001122334455"), PhysicalAddress.Parse("AABBCCDDEEFF"), EthernetType.IPv4)
            {
                PayloadPacket = new IPv4Packet(inbound ? Peer : Target, inbound ? Target : Peer) { PayloadPacket = transport }
            });
            return new DefaultFilterContextParser().CreateContext([capture], direction);
        }
    }
}
