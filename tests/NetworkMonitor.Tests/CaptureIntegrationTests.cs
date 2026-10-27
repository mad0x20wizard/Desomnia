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

    [Fact]
    public void ReassemblyOptionsAreLocalAndBothReassemblersStayRegisteredWhenDisabled()
    {
        var module = new TestModule();
        var rootBuilder = new ContainerBuilder();
        module.Root(rootBuilder, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["IPReassembly:family"] = "none", ["IPReassembly:timeout"] = "23s" }).Build());
        using var root = rootBuilder.Build();
        Assert.False(root.IsRegistered<ReassemblyOptions>());
        var applicationBuilder = new ContainerBuilder();
        // The root bridge must not reintroduce process-wide capture options.
        var bridgeType = typeof(ConfigurableModule).Assembly.GetType("MadWizard.Desomnia.RootContainerBridge", true)!;
        applicationBuilder.RegisterSource((IRegistrationSource)Activator.CreateInstance(bridgeType,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [root], null)!);
        module.Application(applicationBuilder);
        using var application = applicationBuilder.Build();
        Assert.False(application.IsRegistered<ReassemblyOptions>());
        var disabled = new NetworkMonitorConfig { Reassemble = ReassemblyProtocol.None, ReassembleTimeout = TimeSpan.FromSeconds(23) };
        var passthrough = new NetworkMonitorConfig { Reassemble = ReassemblyProtocol.IPv6 | ReassemblyProtocol.Passthrough };
        using var first = application.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag,
            builder => builder.Register(_ => disabled.MakeReassemblyOptions()).AsSelf().SingleInstance());
        using var second = application.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag,
            builder => builder.Register(_ => passthrough.MakeReassemblyOptions()).AsSelf().SingleInstance());
        Assert.Equal(TimeSpan.FromSeconds(23), first.Resolve<ReassemblyOptions>().Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), second.Resolve<ReassemblyOptions>().Timeout);
        Assert.Equal(2, first.Resolve<IEnumerable<IFragmentReassembly>>().Count());
        var a = first.Resolve<CapturePipeline>(); var b = second.Resolve<CapturePipeline>();
        Assert.Same(a, first.Resolve<CapturePipeline>()); Assert.NotSame(a, b);
        foreach (bool ipv6 in new[] { false, true })
            Assert.Null(a.TryCapturePacket(IPReassemblyTests.Fragment(ipv6, IPReassemblyTests.Datagram(), 0, 16, true)));
        Assert.Null(b.TryCapturePacket(IPReassemblyTests.Fragment(false, IPReassemblyTests.Datagram(), 0, 16, true)));
        Assert.IsType<EthernetCapture>(b.TryCapturePacket(IPReassemblyTests.Fragment(true, IPReassemblyTests.Datagram(), 0, 16, true)));
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
