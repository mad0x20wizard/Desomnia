using Autofac;
using Autofac.Builder;
using Autofac.Core;
using MadWizard.Desomnia.Network.HyperV.Configuration;
using MadWizard.Desomnia.Network.HyperV.Manager;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Management.Infrastructure;
using PacketDotNet;
using SharpPcap;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.HyperV.Tests
{
    public class HyperVDeviceSwitcherTests
    {
        private const VirtualTraffic Both = VirtualTraffic.Internal | VirtualTraffic.External;
        private static readonly NetworkIdentity PhysicalIdentity = new("{B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7}");
        private static readonly PhysicalAddress HostAddress = PhysicalAddress.Parse("020000000001");
        private static readonly PhysicalAddress VMAddress = PhysicalAddress.Parse("020000000002");
        private static readonly PhysicalAddress NetworkAddress = PhysicalAddress.Parse("020000000003");

        [Theory]
        [InlineData(VirtualTraffic.None)]
        [InlineData(VirtualTraffic.Internal)]
        [InlineData(VirtualTraffic.External)]
        [InlineData(Both)]
        public void ExternalSwitchResolvesCompositeWithConfiguredCaptureAndSending(VirtualTraffic mode)
        {
            using var fixture = new Fixture(mode);
            var resolved = fixture.Resolve();
            using var selected = Assert.IsAssignableFrom<ILiveDevice>(resolved.Device);
            Assert.NotSame(fixture.Original, selected);
            Assert.NotSame(fixture.Physical, selected);
            Assert.Equal(fixture.Physical.Name, selected.Name);
            Assert.Equal(fixture.Physical.Description, selected.Description);
            Assert.Same(fixture.Interface, resolved.Interface);
            Assert.Same(fixture.Marker, resolved.Marker);
            Assert.Equal(fixture.Interface.Identity, fixture.Discovery.RequestedInterface);
            Assert.DoesNotContain(fixture.Logger.Messages, message => message.Level == LogLevel.Warning);

            var received = 0;
            selected.OnPacketArrival += (sender, capture) =>
            {
                Assert.Same(selected, sender);
                Assert.Same(selected, capture.Device);
                received++;
            };
            selected.Open(new DeviceConfiguration());
            selected.StartCapture();
            Assert.Equal(mode != VirtualTraffic.External, fixture.Original.Started);
            Assert.Equal(mode is VirtualTraffic.External or Both, fixture.Physical.Started);

            var local = Frame(HostAddress, VMAddress);
            fixture.Original.Receive(local);
            Assert.Equal(mode is VirtualTraffic.Internal or Both ? 1 : 0, received);
            fixture.Physical.Receive(local);
            Assert.Equal(mode == VirtualTraffic.None ? 0 : 1, received);

            var nonVM = Frame(NetworkAddress, HostAddress);
            fixture.Original.Receive(nonVM);
            fixture.Physical.Receive(nonVM);
            Assert.Equal(mode == VirtualTraffic.None ? 1 : 2, received);

            selected.SendPacket(local);
            Assert.Equal(mode is VirtualTraffic.Internal or Both ? 1 : 0, fixture.Original.Sent);
            Assert.Equal(mode is VirtualTraffic.Internal or Both ? 0 : 1, fixture.Physical.Sent);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingOrPartialPhysicalDeviceMatchKeepsOriginalAndWarns(bool partialMatch)
        {
            using var fixture = new Fixture(includePhysical: partialMatch);
            if (partialMatch)
                fixture.Physical.Name += "_other";

            Assert.Same(fixture.Original, fixture.Resolve().Device);
            var warning = Assert.Single(fixture.Logger.Messages, message => message.Level == LogLevel.Warning);
            Assert.Contains("No Npcap device was found", warning.Message);
            Assert.Contains("Ethernet", warning.Message);
            Assert.Contains(PhysicalIdentity.Id, warning.Message);
            Assert.Contains("binding may not be installed or enabled", warning.Message);
            Assert.Equal(0, fixture.Discovery.AddressQueries);
        }

        [Theory]
        [InlineData(null)]
        [InlineData((int)HyperVSwitchType.Internal)]
        [InlineData((int)HyperVSwitchType.Private)]
        public void NonExternalInterfacesPassThroughWithoutEnumeratingNpcap(int? switchType)
        {
            using var fixture = new Fixture();
            fixture.Discovery.IsSwitch = switchType.HasValue;
            fixture.Discovery.SwitchType = (HyperVSwitchType?)switchType ?? HyperVSwitchType.External;

            Assert.Same(fixture.Original, fixture.Resolve().Device);
            Assert.Equal(0, fixture.Discovery.DeviceEnumerations);
            Assert.Empty(fixture.Logger.Messages);
        }

        [Fact]
        public void SwitchWithoutPhysicalInterfaceKeepsOriginal()
        {
            using var fixture = new Fixture();
            fixture.Discovery.PhysicalInterface = null;

            Assert.Same(fixture.Original, fixture.Resolve().Device);
            Assert.Equal(0, fixture.Discovery.DeviceEnumerations);
            Assert.DoesNotContain(fixture.Logger.Messages, message => message.Level == LogLevel.Warning);
        }

        [Fact]
        public void AlreadySelectedPhysicalDeviceIsNotWrapped()
        {
            using var fixture = new Fixture();

            Assert.Same(fixture.Physical, fixture.Resolve(device: fixture.Physical).Device);
            Assert.Equal(0, fixture.Discovery.AddressQueries);
            Assert.DoesNotContain(fixture.Logger.Messages, message => message.Level == LogLevel.Warning);
        }

        [Theory]
        [InlineData("discovery")]
        [InlineData("physical-interface")]
        [InlineData("vm-addresses")]
        [InlineData("invalid-address")]
        public void DiscoveryFailuresKeepOriginalAndWarn(string failure)
        {
            using var fixture = new Fixture();
            switch (failure)
            {
                case "discovery":
                    fixture.Discovery.DiscoveryError = new CimException("Discovery unavailable");
                    break;
                case "physical-interface":
                    fixture.Discovery.PhysicalInterfaceError = new NotSupportedException("Physical interface unavailable");
                    break;
                case "vm-addresses":
                    fixture.Discovery.VirtualMachineError = new InvalidOperationException("VM enumeration failed");
                    break;
                case "invalid-address":
                    fixture.Discovery.VirtualMachineAddresses = [PhysicalAddress.None];
                    break;
            }

            var resolved = fixture.Resolve();
            Assert.Same(fixture.Original, resolved.Device);
            Assert.Same(fixture.Marker, resolved.Marker);
            var warning = Assert.Single(fixture.Logger.Messages, message => message.Level == LogLevel.Warning);
            Assert.Contains("keeping capture device", warning.Message);
            Assert.NotNull(warning.Exception);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void MissingParametersContinueWithoutQueryingHyperV(bool withInterface, bool withDevice)
        {
            using var fixture = new Fixture();
            var resolved = fixture.Resolve(withInterface, withDevice);

            Assert.Same(withInterface ? fixture.Interface : null, resolved.Interface);
            Assert.Same(withDevice ? fixture.Original : null, resolved.Device);
            Assert.Same(fixture.Marker, resolved.Marker);
            Assert.Null(fixture.Discovery.RequestedInterface);
            Assert.Equal(0, fixture.Discovery.DeviceEnumerations);
            Assert.Empty(fixture.Logger.Messages);
        }

        public sealed class ResolvedDevice(object marker, INetworkInterface? @interface = null, ILiveDevice? device = null)
        {
            public object Marker { get; } = marker;
            public INetworkInterface? Interface { get; } = @interface;
            public ILiveDevice? Device { get; } = device;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly IContainer _container;
            internal readonly object Marker = new();
            internal readonly HostInterface Interface = new();
            internal readonly CaptureDevice Original = new(@"\Device\NPF_{F5487078-1B80-4868-B151-0788C0A89003}", "Renamed host adapter");
            internal readonly CaptureDevice Physical = new($@"\device\npf_{PhysicalIdentity.Id.ToLowerInvariant()}", "Physical adapter");
            internal readonly RecordingLogger Logger = new();
            internal readonly HyperVDiscovery Discovery;

            internal Fixture(VirtualTraffic mode = Both, bool includePhysical = true)
            {
                var builder = new ContainerBuilder();
                builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
                builder.RegisterInstance(Logger).As<ILogger<HyperVDeviceSwitcher>>();
                builder.RegisterModule(new PluginModule());
                builder.RegisterType<ResolvedDevice>().ConfigurePipeline(pipeline =>
                    pipeline.Use(new HyperVDeviceSwitcher { WatchVirtualTraffic = mode }));
                _container = builder.Build(ContainerBuildOptions.IgnoreStartableComponents);
                Discovery = new HyperVDiscovery(_container.Resolve<HyperVManager>(),
                    includePhysical ? [Original, Physical] : [Original])
                {
                    PhysicalInterface = (PhysicalIdentity, "Ethernet"),
                    VirtualMachineAddresses = [VMAddress]
                };
            }

            internal ResolvedDevice Resolve(bool withInterface = true, bool withDevice = true, ILiveDevice? device = null)
            {
                var parameters = new List<Parameter> { new TypedParameter(typeof(object), Marker) };
                if (withInterface)
                    parameters.Add(new TypedParameter(typeof(INetworkInterface), Interface));
                if (withDevice)
                    parameters.Add(new TypedParameter(typeof(ILiveDevice), device ?? Original));
                return _container.Resolve<ResolvedDevice>(parameters);
            }

            public void Dispose()
            {
                Discovery.Dispose();
                _container.Dispose();
            }
        }

        private static byte[] Frame(PhysicalAddress source, PhysicalAddress destination)
        {
            var frame = new byte[64];
            destination.GetAddressBytes().CopyTo(frame, 0);
            source.GetAddressBytes().CopyTo(frame, 6);
            frame[12] = 0x88;
            frame[13] = 0xb5;
            return frame;
        }

        private sealed class Header : ICaptureHeader
        {
            public PosixTimeval Timeval { get; } = new();
        }

        private sealed class CaptureDevice(string name, string description) : ILiveDevice
        {
            public string Name { get; set; } = name;
            public string Description => description;
            public PhysicalAddress MacAddress => throw new InvalidOperationException("Device selection must not read the Npcap MAC.");
            public LinkLayers LinkType => LinkLayers.Ethernet;
            public TimestampResolution TimestampResolution => TimestampResolution.Microsecond;
            public string? LastError => null;
            public ICaptureStatistics? Statistics => null;
            public string? Filter { get; set; }
            public bool Started { get; private set; }
            public TimeSpan StopCaptureTimeout { get; set; }
            internal int Sent;
            public event PacketArrivalEventHandler? OnPacketArrival;
            public event CaptureStoppedEventHandler? OnCaptureStopped;
            public void Open(DeviceConfiguration configuration) { }
            public void StartCapture() => Started = true;
            public void StopCapture()
            {
                if (!Started) return;
                Started = false;
                OnCaptureStopped?.Invoke(this, CaptureStoppedEventStatus.CompletedWithoutError);
            }
            internal void Receive(byte[] packet) => OnPacketArrival?.Invoke(this, new PacketCapture(this, new Header(), packet));
            public void SendPacket(ReadOnlySpan<byte> packet, ICaptureHeader? header = null) => Sent++;
            public void Capture() => throw new NotSupportedException();
            public GetPacketStatus GetNextPacket(out PacketCapture capture) => throw new NotSupportedException();
            public void Close() => StopCapture();
            public void Dispose() => Close();
        }

        private sealed class HostInterface : INetworkInterface
        {
            public NetworkIdentity Identity => new("{F5487078-1B80-4868-B151-0788C0A89003}");
            public string Name => "Renamed host adapter";
            public PhysicalAddress PhysicalAddress => HostAddress;
            public OperationalStatus Status => OperationalStatus.Up;
            public NetworkInterfaceType Type => NetworkInterfaceType.Ethernet;
            public IReadOnlyList<UnicastIPAddressInformation> Addresses => throw new InvalidOperationException("Device selection must not read IP addresses.");
            public IReadOnlyList<GatewayIPAddressInformation> Gateways => [];
            public string? DNSSuffix => null;
            public int? IPv6ScopeIndex => null;
            public string? SSID => null;
            public bool IsDisabled => false;
            public bool? ShouldBeDisabled { get; set; }
        }

        private sealed class RecordingLogger : ILogger<HyperVDeviceSwitcher>
        {
            internal readonly List<(LogLevel Level, string Message, Exception? Exception)> Messages = [];
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => Messages.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

