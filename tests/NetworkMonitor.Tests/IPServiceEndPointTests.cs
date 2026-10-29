using MadWizard.Desomnia.Network.Datagram;
using MadWizard.Desomnia.Network.Device;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class IPServiceEndPointTests
{
    [Theory]
    [InlineData(IPProtocol.TCP)]
    [InlineData(IPProtocol.UDP)]
    public void ProtocolIsPreservedInServiceConversionAndFormatting(IPProtocol protocol)
    {
        var endpoint = new IPServiceEndPoint(FilterParserPackets.Peer, protocol, 80);
        IPService service = endpoint;

        Assert.Equal(protocol, endpoint.Protocol);
        Assert.Equal(new IPService(protocol, 80), service);
        Assert.Equal(service.Port, endpoint.Port);
        Assert.EndsWith("/" + protocol.ToString().ToLowerInvariant(), endpoint.ToString());
    }

    [Theory]
    [InlineData(IPProtocol.Unknown)]
    [InlineData(IPProtocol.TCP | IPProtocol.UDP)]
    [InlineData((IPProtocol)4)]
    public void AnEndpointMustIdentifyOneTransportProtocol(IPProtocol protocol)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IPServiceEndPoint(FilterParserPackets.Peer, protocol, 80));
    }

    [Fact]
    public void EndpointKeysIncludeAddressPortAndProtocol()
    {
        var tcp = new IPServiceEndPoint(FilterParserPackets.Peer, IPProtocol.TCP, 80);
        var same = new IPServiceEndPoint(IPAddress.Parse("192.0.2.10"), IPProtocol.TCP, 80);
        var udp = new IPServiceEndPoint(tcp.Address, IPProtocol.UDP, 80);
        var otherPort = new IPServiceEndPoint(tcp.Address, IPProtocol.TCP, 81);
        var otherAddress = new IPServiceEndPoint(FilterParserPackets.Watched, IPProtocol.TCP, 80);
        var values = new Dictionary<IPEndPoint, string> { [tcp] = "tcp", [udp] = "udp" };

        Assert.Equal(tcp, same);
        Assert.Equal(tcp.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(tcp, udp);
        Assert.NotEqual(tcp, otherPort);
        Assert.NotEqual(tcp, otherAddress);
        Assert.Equal("tcp", values[same]);
        Assert.Equal("udp", values[new IPServiceEndPoint(tcp.Address, IPProtocol.UDP, 80)]);
    }

    private sealed class ObservedDatagramService() : DatagramService(80)
    {
        public int Received { get; private set; }
        protected override void ProcessDatagram(DatagramPacket datagram) => Received++;
    }

    [Theory]
    [InlineData(IPProtocol.TCP, 0)]
    [InlineData(IPProtocol.UDP, 1)]
    public void DatagramInletStillAcceptsOnlyUdp(IPProtocol protocol, int expected)
    {
        var device = (NetworkDevice)RuntimeHelpers.GetUninitializedObject(typeof(NetworkDevice));
        typeof(NetworkDevice).GetField("<Interface>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(device, new FakeNetworkInterface("test"));
        var service = new ObservedDatagramService
        {
            WireLogger = NullLogger<DatagramService>.Instance, Device = device, Network = null!
        };
        var capture = protocol == IPProtocol.TCP ? FilterParserPackets.Tcp(1, "data") : FilterParserPackets.Udp("data");

        ((INetworkService)service).ProcessCapture(capture);

        Assert.Equal(expected, service.Received);
    }
}
