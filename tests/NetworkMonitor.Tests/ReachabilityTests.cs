using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class ReachabilityTests
{
    private static readonly IPAddress Address = FilterParserPackets.Peer;
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private sealed class TestLocalRange(bool local) : LocalNetworkRange
    {
        public override bool Contains(IPAddress ip) => local;
    }

    private static (ReachabilityService Service, ReachabilityCache Cache, NetworkHost Host) Create(bool local = true)
    {
        var network = new NetworkSegment
        {
            Logger = NullLogger<NetworkSegment>.Instance, Device = null!, Ranges = null!,
            LocalRange = new TestLocalRange(local) { Device = null! }
        };
        var monitor = new NetworkMonitor
        {
            Logger = NullLogger<NetworkMonitor>.Instance, Name = "test", Network = network,
            Device = null!, Janitor = null!, Pipeline = null!, Options = default
        };
        var cache = new ReachabilityCache();
        var service = new ReachabilityService
        {
            Logger = NullLogger<ReachabilityService>.Instance, Monitor = monitor,
            Network = network, Device = null!, Cache = cache
        };
        var host = new NetworkHost("peer") { Network = network };
        host.AddAddress(Address);
        network.AddHost(host);
        return (service, cache, host);
    }

    private static IPServiceEndPoint Transport(IPProtocol protocol, ushort port = 80, IPAddress? address = null)
        => new(address ?? Address, protocol, port);

    [Theory]
    [InlineData(IPProtocol.TCP)]
    [InlineData(IPProtocol.UDP)]
    public void NotifyRecordsHostAndSourceEndpointWithoutConfusingProtocols(IPProtocol protocol)
    {
        var (service, cache, host) = Create();
        var endpoint = Transport(protocol);

        service.Notify(host, FilterParserPackets.PeerMac, endpoint);

        Assert.True(cache.Read(Address, MaxAge));
        Assert.True(cache.Read(Transport(protocol), MaxAge));
        Assert.True(cache.Read(new IPEndPoint(Address, 80), MaxAge));
        Assert.Null(cache.Read(Transport(protocol == IPProtocol.TCP ? IPProtocol.UDP : IPProtocol.TCP), MaxAge));
        Assert.Null(cache.Read(Transport(protocol, 81), MaxAge));
    }

    [Fact]
    public void PortZeroOnlyEstablishesHostReachability()
    {
        var (service, cache, host) = Create();
        var endpoint = new IPEndPoint(Address, 0);
        using var hostTest = new ReachabilityTest([Address], MaxAge);
        using var serviceTest = new ServiceReachabilityTest(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), MaxAge);

        service.Notify(host, FilterParserPackets.PeerMac, endpoint);
        hostTest.NotifyReachable(endpoint);
        serviceTest.NotifyReachable(endpoint);

        Assert.True(cache.Read(Address, MaxAge));
        Assert.True(cache.Read(endpoint, MaxAge));
        Assert.Null(cache.Read(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), MaxAge));
        Assert.NotNull(hostTest[Address]);
        Assert.Null(serviceTest[Address]);
    }

    [Fact]
    public void NonzeroNotificationRequiresAProtocolSpecificEndpoint()
    {
        var (service, cache, host) = Create();

        Assert.Throws<ArgumentException>(() => service.Notify(host, FilterParserPackets.PeerMac, new IPEndPoint(Address, 80)));
        Assert.Null(cache.Read(Address, MaxAge));
    }

    [Theory]
    [InlineData(IPProtocol.TCP)]
    [InlineData(IPProtocol.UDP)]
    public void CapturedTrafficReportsTheSourcePort(IPProtocol protocol)
    {
        var (service, cache, _) = Create();
        var capture = protocol == IPProtocol.TCP ? FilterParserPackets.Tcp(1, "data") : FilterParserPackets.Udp("data");

        ((INetworkService)service).ProcessCapture(capture);

        Assert.True(cache.Read(Transport(protocol, 50000), MaxAge));
        Assert.Null(cache.Read(Transport(protocol, 80), MaxAge));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MismatchingMacIsRejectedOnlyForLocalAddresses(bool local)
    {
        var (service, cache, host) = Create(local);
        host.PhysicalAddress = FilterParserPackets.WatchedMac;

        service.Notify(host, FilterParserPackets.PeerMac, new IPEndPoint(Address, 0));

        Assert.Equal(local ? (bool?)null : true, cache.Read(Address, MaxAge));
    }

    [Theory]
    [InlineData(IPProtocol.TCP, 80, true, true)]
    [InlineData(IPProtocol.UDP, 80, true, false)]
    [InlineData(IPProtocol.TCP, 81, true, false)]
    [InlineData(IPProtocol.TCP, 0, true, false)]
    [InlineData(IPProtocol.TCP, 80, false, false)]
    public void ServiceTestRequiresMatchingAddressPortAndProtocol(IPProtocol protocol, ushort port, bool sameAddress, bool matches)
    {
        using var test = new ServiceReachabilityTest(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), MaxAge);

        test.NotifyReachable(Transport(protocol, port, sameAddress ? Address : FilterParserPackets.Watched));

        Assert.Equal(matches, test[Address].HasValue);
    }

    [Theory]
    [InlineData(IPProtocol.TCP)]
    [InlineData(IPProtocol.UDP)]
    public void ServiceTestAcceptsItsConfiguredTransport(IPProtocol protocol)
    {
        using var test = new ServiceReachabilityTest(Transport(protocol), MaxAge);

        test.NotifyReachable(Transport(protocol));

        Assert.NotNull(test[Address]);
    }

    [Fact]
    public void ServiceCacheReadsDoNotFallBackToAnotherProtocolOrHostActivity()
    {
        var cache = new ReachabilityCache();
        cache.Write(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), false);
        cache.Write(new IPServiceEndPoint(Address, IPProtocol.UDP, 80), true);
        using var tcp = new ServiceReachabilityTest(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), MaxAge);
        using var udp = new ServiceReachabilityTest(new IPServiceEndPoint(Address, IPProtocol.UDP, 80), MaxAge);

        Assert.True(cache.Read(Address, MaxAge));
        Assert.False(cache.Read(tcp)[Address]);
        Assert.True(cache.Read(udp)[Address]);
        Assert.True(cache.Read(new IPEndPoint(Address, 80), MaxAge));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimedOutTestsWriteTheirEndpointToTheCache(bool serviceTest)
    {
        var (service, cache, _) = Create();
        ReachabilityTest test = serviceTest
            ? new ServiceReachabilityTest(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), TimeSpan.Zero)
            : new ReachabilityTest([Address], TimeSpan.Zero);

        await Assert.ThrowsAsync<HostTimeoutException>(() => service.Until(test));

        Assert.False(cache.Read(new IPEndPoint(Address, 0), MaxAge));
        Assert.Equal(serviceTest ? false : (bool?)null, cache.Read(new IPServiceEndPoint(Address, IPProtocol.TCP, 80), MaxAge));
        Assert.Null(cache.Read(new IPServiceEndPoint(Address, IPProtocol.UDP, 80), MaxAge));
    }
}