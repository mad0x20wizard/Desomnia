using Autofac;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Middleware;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;
using static MadWizard.Desomnia.Network.Tests.BerkeleyPacketFilterTests;

namespace MadWizard.Desomnia.Network.Tests;

public class BPFCompilationTests
{
    [PcapTheory]
    [InlineData(0)]
    public void MiddlewareSuppliesDetectedLimitBeforeFilterCompilation(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" }, new DefaultBPFLimit(() => Count("arp") - 1));
        Assert.Throws<InvalidOperationException>(() => fixture.Device.StartCapture());
        Assert.Empty(fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void MiddlewarePreservesExplicitLimitWithoutRunningDetection(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp", InstructionLimit = Count("arp") },
            new DefaultBPFLimit(() => throw new Xunit.Sdk.XunitException("An explicit limit must bypass detection")));
        fixture.Device.StartCapture();
        Assert.Equal(["arp"], fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void UnknownPlatformLimitLeavesCustomExpressionUnrestricted(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" }, new DefaultBPFLimit(() => null));
        fixture.Device.StartCapture();
        Assert.Equal(["arp"], fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void CustomExpressionOverBudgetThrowsBeforeInstallation(int _)
    {
        int count = Count("arp");
        using var fixture = new Fixture(new() { Expression = "arp", InstructionLimit = count - 1 });
        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Device.StartCapture());
        Assert.Contains($"requires {count} instructions", exception.Message);
        Assert.Empty(fixture.Native.Installed);
        Assert.Null(fixture.Native.Filter);
    }

    [PcapTheory]
    [InlineData(0)]
    public async Task CustomExpressionAtBudgetIsInstalledAndIgnoresTrafficRequests(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp", InstructionLimit = Count("arp") });
        fixture.Device.StartCapture();
        using (var request = new TrafficFilterRequest([new TCPTrafficType(80)], fixture.Filter))
            Assert.Equal("arp", fixture.Native.Filter);
        fixture.Filter.Refresh();
        Assert.Equal(["arp"], fixture.Native.Installed);
        await fixture.Service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
        Assert.Equal("", fixture.Native.Filter);
    }

    [PcapTheory]
    [InlineData(0)]
    public void InvalidCustomExpressionDoesNotFallBackToUnfilteredCapture(int _)
    {
        using var fixture = new Fixture(new() { Expression = "(" });
        Assert.Throws<PcapException>(() => fixture.Device.StartCapture());
        Assert.Empty(fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void CustomInstallationFailurePropagates(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" });
        fixture.Native.FailInstallation = true;
        Assert.Throws<InvalidOperationException>(() => fixture.Device.StartCapture());
        Assert.Empty(fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void AutomaticFilterSimplifiesBeforeInstallingAnOversizedProgram(int _)
    {
        const int limit = 32;
        using var fixture = new Fixture(new() { InstructionLimit = limit });
        using var request = new TrafficFilterRequest([new IPv4TrafficType(),
            .. Enumerable.Range(14000, 100).Select(port => (ITrafficType)new UDPTrafficType((ushort)port))], fixture.Filter);
        fixture.Device.StartCapture();

        string expression = Assert.Single(fixture.Native.Installed);
        Assert.NotEmpty(expression);
        Assert.Contains("udp", expression);
        Assert.DoesNotContain("port", expression);
        Assert.InRange(Count(expression), 1, limit);
        using var program = BpfProgram.Create(LinkLayers.Ethernet, expression);
        Assert.False(program.Matches(FilterParserPackets.Arp().Single().CapturedBytes));
    }

    [PcapTheory]
    [InlineData(0)]
    public void AutomaticFilterMayFallBackToUnfilteredCaptureWithinBudget(int _)
    {
        using var fixture = new Fixture(new() { InstructionLimit = 1 });
        using var request = new TrafficFilterRequest([new ARPTrafficType()], fixture.Filter);
        fixture.Device.StartCapture();
        Assert.Equal([""], fixture.Native.Installed);
        Assert.Equal(1, Count(fixture.Native.Filter!));
    }

    [PcapTheory]
    [InlineData(0)]
    public void FilterIsInstalledBeforeCaptureAndReappliedAfterResume(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" });
        fixture.Device.StartCapture();
        Assert.Equal(["arp"], fixture.Native.FiltersAtStart);
        fixture.Device.StopCapture();
        fixture.Device.StartCapture();
        Assert.Equal(["arp", "arp"], fixture.Native.Installed);
        Assert.Equal(["arp", "arp"], fixture.Native.FiltersAtStart);
    }

    [PcapTheory]
    [InlineData(CaptureStoppedEventStatus.CompletedWithoutError)]
    [InlineData(CaptureStoppedEventStatus.ErrorWhileCapturing)]
    public void UnexpectedStopReappliesFilterBeforeAutomaticRestart(CaptureStoppedEventStatus status)
    {
        using var fixture = new Fixture(new() { Expression = "arp" });
        fixture.Device.StartCapture();
        fixture.Native.StopUnexpectedly(status);
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal(["arp", "arp"], fixture.Native.Installed);
        Assert.Equal(["arp", "arp"], fixture.Native.FiltersAtStart);
    }

    [PcapTheory]
    [InlineData(0)]
    public void FilterSurvivesFailedReopenAndSubsequentRetry(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" });
        fixture.Device.StartCapture();
        fixture.Native.FailNextOpen = true;
        Assert.Throws<InvalidOperationException>(() => fixture.Device.Restart());
        Assert.False(fixture.Device.IsCapturing);
        Assert.Null(fixture.Native.Filter);
        fixture.Filter.Refresh();
        Assert.Single(fixture.Native.Installed);
        fixture.Device.Restart();
        Assert.True(fixture.Device.IsCapturing);
        Assert.Equal(["arp", "arp"], fixture.Native.FiltersAtStart);
        Assert.Equal(["arp", "arp"], fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void RequestsWhileStoppedAreAppliedOnNextStart(int _)
    {
        using var fixture = new Fixture(default);
        fixture.Device.StartCapture();
        fixture.Device.StopCapture();
        using var request = new TrafficFilterRequest([new ARPTrafficType()], fixture.Filter);
        fixture.Filter.Refresh();
        Assert.Single(fixture.Native.Installed);
        fixture.Device.StartCapture();
        Assert.Equal("arp", fixture.Native.Filter);
        Assert.Equal(2, fixture.Native.Installed.Count);
    }

    [PcapTheory]
    [InlineData(0)]
    public void RejectedStaticFilterPreventsCaptureAndCanBeRetried(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" });
        fixture.Native.FailInstallation = true;
        Assert.Throws<InvalidOperationException>(() => fixture.Device.StartCapture());
        Assert.False(fixture.Device.IsCapturing);
        Assert.Equal(0, fixture.Native.StartCalls);
        fixture.Native.FailInstallation = false;
        fixture.Device.StartCapture();
        Assert.Equal(["arp"], fixture.Native.FiltersAtStart);
        Assert.Single(fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void OverBudgetStaticFilterPreventsCapture(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp", InstructionLimit = Count("arp") - 1 });
        Assert.Throws<InvalidOperationException>(() => fixture.Device.StartCapture());
        Assert.False(fixture.Device.IsCapturing);
        Assert.Equal(0, fixture.Native.StartCalls);
        Assert.Empty(fixture.Native.Installed);
    }

    [PcapTheory]
    [InlineData(0)]
    public void DisposalUnsubscribesFromCaptureLifecycle(int _)
    {
        using var fixture = new Fixture(new() { Expression = "arp" });
        fixture.Filter.Dispose();
        fixture.Device.StartCapture();
        Assert.Empty(fixture.Native.Installed);
        Assert.True(fixture.Device.IsCapturing);
    }

    private static int Count(string expression)
    {
        using var program = BpfProgram.Create(LinkLayers.Ethernet, expression);
        return Marshal.ReadInt32(program.DangerousGetHandle());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IContainer _container;
        public NetworkDevice Device { get; }
        public FilterDevice Native { get; }
        public BerkeleyPacketFilter Filter { get; }
        public INetworkService Service => Filter;

        public Fixture(BPFOptions options, DefaultBPFLimit? middleware = null)
        {
            var live = DispatchProxy.Create<ILiveDevice, FilterDevice>();
            Native = (FilterDevice)live;
            Device = new(NullLogger<NetworkDevice>.Instance, new FakeNetworkInterface("test"), live);
            var builder = new ContainerBuilder();
            builder.RegisterInstance(Device).ExternallyOwned();
            builder.RegisterType<BerkeleyPacketFilter>()
                .OnPreparing(e => e.Parameters = [.. e.Parameters, TypedParameter.From(options)])
                .ConfigurePipeline(p => p.Use(middleware ?? new DefaultBPFLimit()))
                .WithParameter(TypedParameter.From(default(ReassemblyOptions)))
                .WithProperty(nameof(BerkeleyPacketFilter.Logger), NullLogger<BerkeleyPacketFilter>.Instance)
                .WithProperty(nameof(BerkeleyPacketFilter.Monitor), new Lazy<NetworkMonitor>(() => null!));
            _container = builder.Build();
            Filter = _container.Resolve<BerkeleyPacketFilter>();
        }

        public void Dispose()
        {
            _container.Dispose();
            ((IDisposable)Device).Dispose();
        }
    }

    public class FilterDevice : ReconfigurationLifecycleTests.NativeDevice
    {
        public List<string> Installed { get; } = [];
        public bool FailInstallation;
        public List<string?> FiltersAtStart { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "StartCapture") FiltersAtStart.Add(Filter);
            if (method.Name == "get_LinkType") return LinkLayers.Ethernet;
            if (method.Name == "set_Filter")
            {
                if (FailInstallation) throw new InvalidOperationException("Cannot install filter");
                Installed.Add((string)args![0]!);
            }
            return base.Invoke(method, args);
        }
    }
}
