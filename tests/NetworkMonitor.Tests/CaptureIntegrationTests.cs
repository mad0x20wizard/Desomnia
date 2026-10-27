using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Discovery.BuiltIn;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class CaptureIntegrationTests
{
    private sealed class TestModule : Network.Module
    {
        public void Root(ContainerBuilder builder, IConfiguration configuration) => base.LoadOnce(builder, configuration);
        public void Application(ContainerBuilder builder) => base.Load(builder, new ModuleConfig<NetworkMonitorConfig>());
    }

    [Theory]
    [InlineData(null, null, 3, 60)]
    [InlineData("none", "23s", 0, 23)]
    [InlineData("IPv4|Passthrough", "2min", 5, 120)]
    [InlineData("IPv6|Passthrough", "30s", 6, 30)]
    [InlineData("IPv4|IPv6|Passthrough", "1s", 7, 1)]
    public void RootConfigurationBindsUnifiedOptions(string? family, string? timeout, int expectedFamily, int seconds)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["IPReassembly:family"] = family, ["IPReassembly:timeout"] = timeout }
            .Where(pair => pair.Value is not null)).Build();
        var builder = new ContainerBuilder();
        new TestModule().Root(builder, configuration);
        using var root = builder.Build();
        var options = root.Resolve<IPReassemblyOptions>();
        Assert.Equal((IPReassemblyFamily)expectedFamily, options.Family);
        Assert.Equal(TimeSpan.FromSeconds(seconds), options.Timeout);
    }

    [Theory]
    [InlineData("3", "0s", nameof(IPReassemblyOptions.Timeout))]
    [InlineData("8", "60s", nameof(IPReassemblyOptions.Family))]
    public void InvalidRootOptionsFailDuringBinding(string family, string timeout, string property)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["IPReassembly:family"] = family, ["IPReassembly:timeout"] = timeout }).Build();
        var exception = Assert.Throws<TargetInvocationException>(() => new TestModule().Root(new ContainerBuilder(), configuration));
        var validation = Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        Assert.Equal(property, validation.ParamName);
    }

    [Fact]
    public void RootOptionsReachEveryNetworkAndBothReassemblersStayRegisteredWhenDisabled()
    {
        var module = new TestModule();
        var rootBuilder = new ContainerBuilder();
        module.Root(rootBuilder, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["IPReassembly:family"] = "none", ["IPReassembly:timeout"] = "23s" }).Build());
        using var root = rootBuilder.Build();
        var applicationBuilder = new ContainerBuilder();
        // Exercise the actual root registration source, including fallback precedence.
        var bridgeType = typeof(ConfigurableModule).Assembly.GetType("MadWizard.Desomnia.RootContainerBridge", true)!;
        applicationBuilder.RegisterSource((IRegistrationSource)Activator.CreateInstance(bridgeType,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [root], null)!);
        module.Application(applicationBuilder);
        using var application = applicationBuilder.Build();
        using var first = application.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag);
        using var second = application.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag);
        Assert.Same(root.Resolve<IPReassemblyOptions>(), first.Resolve<IPReassemblyOptions>());
        Assert.Equal(TimeSpan.FromSeconds(23), first.Resolve<IPReassemblyOptions>().Timeout);
        Assert.Equal(2, first.Resolve<IEnumerable<IFragmentReassembly>>().Count());
        var a = first.Resolve<CapturePipeline>(); var b = second.Resolve<CapturePipeline>();
        Assert.Same(a, first.Resolve<CapturePipeline>()); Assert.NotSame(a, b);
        foreach (bool ipv6 in new[] { false, true })
            Assert.Null(a.TryCapturePacket(IPReassemblyTests.Fragment(ipv6, IPReassemblyTests.Datagram(), 0, 16, true)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutgoingLoopSuppressionDoesNotRewriteFragmentHeaders(bool ipv6)
    {
        var device = (NetworkDevice)RuntimeHelpers.GetUninitializedObject(typeof(NetworkDevice));
        var filter = new LocalPacketFilter(device);
        var original = IPReassemblyTests.Fragment(ipv6, IPReassemblyTests.Datagram(), 0, 16, true);
        var copy = original.MakeCapturedCopy();
        byte[] before = original.CapturedBytes.ToArray();
        Assert.False(filter.FilterOutgoing(copy));
        Assert.Equal(before, copy.CapturedBytes.ToArray());
        Assert.Equal(before, original.CapturedBytes.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutgoingLoopSuppressionAlsoPreservesBareIPFragments(bool ipv6)
    {
        var device = (NetworkDevice)RuntimeHelpers.GetUninitializedObject(typeof(NetworkDevice));
        var filter = new LocalPacketFilter(device);
        var frame = IPReassemblyTests.Fragment(ipv6, IPReassemblyTests.Datagram(), 0, 16, true);
        byte[] before = (byte[])frame.HeaderSegment.Bytes.Clone();

        Assert.False(filter.FilterOutgoing(frame.Extract<IPPacket>()));

        Assert.Equal(before, frame.HeaderSegment.Bytes);
    }

    [Fact]
    public async Task PhysicalAddressDiscoveryReceivesCapturesThroughNetworkService()
    {
        var detector = Detector();
        var host = new NetworkHost("peer") { Network = detector.TestNetwork };
        host.AddAddress(FilterParserPackets.Peer);
        var discovery = ((IPhysicalAddressDiscovery)detector).DiscoverAddress(host);
        Assert.False(discovery.IsCompleted);
        var mapping = new AddressMapping(FilterParserPackets.Peer, FilterParserPackets.PeerMac);
        detector.Response = mapping;
        ((INetworkService)detector).ProcessCapture(FilterParserPackets.Arp());
        await discovery.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(mapping.PhysicalAddress, host.PhysicalAddress);
        Assert.Equal(1, detector.Probes);
    }

    [Fact]
    public async Task SuspendingDiscoveryReleasesItsPendingRequest()
    {
        var detector = Detector();
        var host = new NetworkHost("peer") { Network = detector.TestNetwork };
        host.AddAddress(FilterParserPackets.Peer);
        var discovery = ((IPhysicalAddressDiscovery)detector).DiscoverAddress(host);
        await ((INetworkService)detector).Suspend();
        await discovery.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Null(host.PhysicalAddress);
    }

    private static TestDetector Detector()
    {
        var network = new NetworkSegment { Device = null!, Ranges = null!, Logger = NullLogger<NetworkSegment>.Instance,
            LocalRange = new AllLocal() { Device = null! } };
        return new TestDetector { Device = null!, Network = network, TestNetwork = network, Reachability = null!,
            Logger = NullLogger<TestDetector>.Instance, Options = new DiscoveryOptions { Timeout = TimeSpan.FromSeconds(3) } };
    }
    private sealed class AllLocal : LocalNetworkRange { public override bool Contains(IPAddress ip) => true; }
    private sealed class TestDetector : PhysicalAddressDetector<TestDetector>
    {
        public required NetworkSegment TestNetwork { get; init; }
        public required override ILogger<TestDetector> Logger { protected get; init; }
        public int Probes;
        public AddressMapping? Response;
        protected override AddressFamily Family => AddressFamily.InterNetwork;
        protected override void SendRequest(IPAddress ip) => Probes++;
        protected override AddressMapping? AnalyzeCapture(INetworkCapture packet) => Response;
    }
}
