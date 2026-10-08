using MadWizard.Desomnia.Network.HyperV.Configuration;
using MadWizard.Desomnia.Network.HyperV.Middleware;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using SharpPcap;
using System.Net.NetworkInformation;
using System.Reflection;
using Xunit;

namespace MadWizard.Desomnia.Network.HyperV.Tests
{
    public class HyperVDeviceTests
    {
        private const VirtualTraffic Both = VirtualTraffic.Internal | VirtualTraffic.External;
        private static readonly VirtualTraffic[] Modes = [VirtualTraffic.None, VirtualTraffic.Internal, VirtualTraffic.External, Both];
        private const string Host = "020000000001";
        private const string VM = "020000000002";
        private const string OtherVM = "020000000003";
        private const string Network = "020000000004";
        private const string Broadcast = "FFFFFFFFFFFF";
        private const string Multicast = "333300000001";

        [Theory]
        [InlineData(VM, Host, true)]
        [InlineData(Host, VM, true)]
        [InlineData(OtherVM, Host, true)]
        [InlineData(Host, OtherVM, true)]
        [InlineData(VM, Network, false)]
        [InlineData(Network, VM, false)]
        [InlineData(Network, Host, false)]
        [InlineData(Host, Network, false)]
        [InlineData(Network, Multicast, false)]
        [InlineData(Network, Broadcast, false)]
        [InlineData(Host, Broadcast, false)]
        [InlineData(VM, Broadcast, false)]
        [InlineData(VM, OtherVM, false)]
        public void SelectsCaptureSourceAndFiltersByMode(string source, string destination, bool useVirtual)
        {
            foreach (var mode in Modes)
            {
                var virtualDevice = new CaptureDevice();
                var physicalDevice = new CaptureDevice();
                using var device = Create(virtualDevice, physicalDevice);
                device.WatchVirtualTraffic = mode;
                var packet = Frame(source, destination);
                var received = 0;
                device.OnPacketArrival += (sender, capture) =>
                {
                    Assert.Same(device, sender);
                    Assert.Same(device, capture.Device);
                    Assert.True(capture.Data.SequenceEqual(packet));
                    received++;
                };
                device.StartCapture();
                Assert.Equal(mode != VirtualTraffic.External, virtualDevice.Started);
                Assert.Equal(mode is VirtualTraffic.External or Both, physicalDevice.Started);
                var excluded = mode == VirtualTraffic.None && (source is VM or OtherVM || destination is VM or OtherVM);
                var acceptVirtual = mode switch
                {
                    VirtualTraffic.None => !excluded,
                    VirtualTraffic.Internal => true,
                    VirtualTraffic.External => false,
                    _ => useVirtual
                };
                virtualDevice.Receive(packet);
                Assert.Equal(acceptVirtual ? 1 : 0, received);
                physicalDevice.Receive(packet);
                Assert.Equal(excluded ? 0 : 1, received);
            }
        }

        [Theory]
        [InlineData(Host, VM, true)]
        [InlineData(Network, VM, true)] // Injection may use a masqueraded source.
        [InlineData(Host, OtherVM, true)]
        [InlineData(VM, Network, false)]
        [InlineData(VM, Host, false)]
        [InlineData(Host, Broadcast, false)]
        [InlineData(Host, Multicast, false)]
        public void SelectsSendingDeviceByUnicastDestination(string source, string destination, bool useVirtual)
        {
            foreach (var mode in Modes)
            {
                var virtualDevice = new CaptureDevice();
                var physicalDevice = new CaptureDevice();
                using var device = Create(virtualDevice, physicalDevice);
                device.WatchVirtualTraffic = mode;
                var packet = Frame(source, destination);
                var header = new Header();
                device.SendPacket(packet, header);
                var sendVirtual = useVirtual && mode is VirtualTraffic.Internal or Both;
                Assert.Equal(sendVirtual ? 1 : 0, virtualDevice.Sent.Count);
                Assert.Equal(sendVirtual ? 0 : 1, physicalDevice.Sent.Count);
                var selected = sendVirtual ? virtualDevice : physicalDevice;
                Assert.Equal(packet, Assert.Single(selected.Sent));
                Assert.Same(header, selected.LastSentHeader);
            }
        }

        [Fact]
        public void ReplacingMapChangesCaptureAndSendingWithoutRestart()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice { MacAddress = PhysicalAddress.Parse(Network) };
            using var device = Create(virtualDevice, physicalDevice);
            var received = 0;
            device.OnPacketArrival += (sender, capture) => received++;
            device.StartCapture();
            device.UpdateVirtualMachineAddresses([PhysicalAddress.Parse(Network)]);
            virtualDevice.Receive(Frame(Host, VM));
            virtualDevice.Receive(Frame(Host, Network));
            Assert.Equal(1, received);
            physicalDevice.Receive(Frame(Host, VM));
            physicalDevice.Receive(Frame(Host, Network));
            Assert.Equal(2, received);
            device.SendPacket(Frame(Host, VM));
            device.SendPacket(Frame(Host, Network));
            Assert.Single(virtualDevice.Sent);
            Assert.Single(physicalDevice.Sent);
            Assert.Equal(1, virtualDevice.Starts);
            Assert.Equal(1, physicalDevice.Starts);
        }

        [Fact]
        public void NoneUsesUpdatedVmMapAndFiltersTaggedPackets()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            device.WatchVirtualTraffic = VirtualTraffic.None;
            var received = 0;
            device.OnPacketArrival += (sender, capture) => received++;
            device.StartCapture();
            var oldVM = Frame(VM, Host);
            var newVM = Frame(Host, Network);
            oldVM[12] = newVM[12] = 0x81;
            oldVM[13] = newVM[13] = 0x00;
            virtualDevice.Receive(oldVM);
            Assert.Equal(0, received);
            virtualDevice.Receive(newVM);
            Assert.Equal(1, received);

            device.UpdateVirtualMachineAddresses([PhysicalAddress.Parse(Network)]);
            virtualDevice.Receive(oldVM);
            Assert.Equal(2, received);
            virtualDevice.Receive(newVM);
            Assert.Equal(2, received);
            Assert.Equal(1, virtualDevice.Starts);
            Assert.Equal(0, physicalDevice.Starts);
        }

        [Theory]
        [InlineData(VirtualTraffic.None)]
        [InlineData(VirtualTraffic.Internal)]
        [InlineData(VirtualTraffic.External)]
        public void SingleAdapterModesNeverStartTheOtherDevice(VirtualTraffic mode)
        {
            var virtualDevice = new CaptureDevice { FailStart = mode == VirtualTraffic.External };
            var physicalDevice = new CaptureDevice { FailStart = mode != VirtualTraffic.External };
            using var device = Create(virtualDevice, physicalDevice);
            device.WatchVirtualTraffic = mode;
            var received = 0;
            device.OnPacketArrival += (sender, capture) => received++;
            device.StartCapture();
            var active = mode == VirtualTraffic.External ? physicalDevice : virtualDevice;
            var inactive = mode == VirtualTraffic.External ? virtualDevice : physicalDevice;
            Assert.Equal(0, inactive.Starts);
            Assert.Equal(0, inactive.PacketSubscriptions);
            Assert.Equal(0, inactive.StopSubscriptions);
            for (var length = 0; length < 14; length++)
                active.Receive(new byte[length]);
            // Only None needs a complete Ethernet header to decide whether to exclude a VM.
            Assert.Equal(mode == VirtualTraffic.None ? 0 : 14, received);
        }

        [Fact]
        public void RejectsTruncatedFramesAndHandlesVlanHeaderWithoutParsing()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            var received = 0;
            device.OnPacketArrival += (sender, capture) => received++;
            device.StartCapture();
            for (var length = 0; length < 14; length++)
            {
                virtualDevice.Receive(new byte[length]);
                physicalDevice.Receive(new byte[length]);
            }
            Assert.Equal(0, received);
            Assert.Throws<ArgumentException>(() => device.SendPacket(new byte[13]));
            var tagged = Frame(Host, VM);
            tagged[12] = 0x81;
            tagged[13] = 0x00;
            virtualDevice.Receive(tagged);
            physicalDevice.Receive(tagged);
            Assert.Equal(1, received);
        }

        [Theory]
        [InlineData(VirtualTraffic.None)]
        [InlineData(VirtualTraffic.Internal)]
        [InlineData(VirtualTraffic.External)]
        [InlineData(Both)]
        public void PacketClassificationAndForwardingDoNotAllocate(VirtualTraffic watchVirtualTraffic)
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            device.WatchVirtualTraffic = watchVirtualTraffic;
            var received = 0;
            device.OnPacketArrival += (sender, capture) => received++;
            device.StartCapture();
            var local = Frame(Host, VM);
            var external = Frame(Network, VM);
            var nonVM = Frame(Network, Host);
            void Receive()
            {
                virtualDevice.Receive(local);
                physicalDevice.Receive(local);
                virtualDevice.Receive(external);
                physicalDevice.Receive(external);
                virtualDevice.Receive(nonVM);
                physicalDevice.Receive(nonVM);
            }
            for (var i = 0; i < 1000; i++)
                Receive();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
                Receive();
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
            Assert.Equal(watchVirtualTraffic == VirtualTraffic.None ? 2000 : 6000, received);
        }

        [Fact]
        public void OpenFailureClosesBothDevicesBeforeRetryingWithFewerFlags()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice { FailOpen = true };
            using var device = Create(virtualDevice, physicalDevice, open: false);
            var mode = DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness | DeviceModes.NoCaptureLocal;
            Assert.Throws<InvalidOperationException>(() => device.Open(new DeviceConfiguration { Mode = mode }));
            Assert.False(virtualDevice.Opened);
            Assert.False(physicalDevice.Opened);
            Assert.Equal(1, virtualDevice.Closes);
            Assert.Equal(1, physicalDevice.Closes);
            physicalDevice.FailOpen = false;
            device.Open(new DeviceConfiguration { Mode = DeviceModes.Promiscuous });
            Assert.Equal(new[] { mode, DeviceModes.Promiscuous }, virtualDevice.OpenModes);
            Assert.Equal(virtualDevice.OpenModes, physicalDevice.OpenModes);
        }

        [Fact]
        public void AppliesNoCaptureLocalAndFilterToBothAndRollsBackFailedFilter()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice, open: false);
            var mode = DeviceModes.Promiscuous | DeviceModes.NoCaptureLocal;
            device.Open(new DeviceConfiguration { Mode = mode });
            Assert.Equal(mode, Assert.Single(virtualDevice.OpenModes));
            Assert.Equal(mode, Assert.Single(physicalDevice.OpenModes));
            device.Filter = "ip";
            physicalDevice.RejectedFilter = "ip6";
            Assert.Throws<InvalidOperationException>(() => device.Filter = "ip6");
            Assert.Equal("ip", device.Filter);
            Assert.Equal("ip", virtualDevice.Filter);
            Assert.Equal("ip", physicalDevice.Filter);
            device.StopCaptureTimeout = TimeSpan.FromSeconds(2);
            Assert.Equal(device.StopCaptureTimeout, virtualDevice.StopCaptureTimeout);
            Assert.Equal(device.StopCaptureTimeout, physicalDevice.StopCaptureTimeout);
        }

        [Fact]
        public void StartFailureStopsBothAndDetachesCallbacks()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice { FailStart = true };
            using var device = Create(virtualDevice, physicalDevice);
            Assert.Throws<InvalidOperationException>(device.StartCapture);
            Assert.False(device.Started);
            Assert.False(virtualDevice.Started);
            Assert.False(physicalDevice.Started);
            Assert.Equal(0, virtualDevice.PacketSubscriptions);
            Assert.Equal(0, physicalDevice.PacketSubscriptions);
            Assert.Equal(0, virtualDevice.StopSubscriptions);
            Assert.Equal(0, physicalDevice.StopSubscriptions);
        }

        [Fact]
        public void StopRaisesOneEventAndCanBeRepeatedAndRestarted()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            var stopped = 0;
            device.OnCaptureStopped += (sender, status) =>
            {
                Assert.Same(device, sender);
                Assert.Equal(CaptureStoppedEventStatus.CompletedWithoutError, status);
                Assert.False(device.Started);
                Assert.False(virtualDevice.Started);
                Assert.False(physicalDevice.Started);
                stopped++;
            };
            device.StartCapture();
            device.StartCapture();
            device.StopCapture();
            device.StopCapture();
            Assert.Equal(1, stopped);
            device.StartCapture();
            Assert.Equal(1, virtualDevice.PacketSubscriptions);
            Assert.Equal(1, physicalDevice.PacketSubscriptions);
            device.StopCapture();
            Assert.Equal(2, stopped);
        }

        [Theory]
        [InlineData(VirtualTraffic.None)]
        [InlineData(VirtualTraffic.Internal)]
        [InlineData(VirtualTraffic.External)]
        [InlineData(Both)]
        public async Task UnexpectedStopJoinsBothCaptureThreadsBeforeAllowingRestart(VirtualTraffic watchVirtualTraffic)
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice { LastError = "capture failed" };
            using var device = Create(virtualDevice, physicalDevice);
            device.WatchVirtualTraffic = watchVirtualTraffic;
            var failingDevice = watchVirtualTraffic is VirtualTraffic.None or VirtualTraffic.Internal ? virtualDevice : physicalDevice;
            failingDevice.LastError = "capture failed";
            var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            device.OnCaptureStopped += (sender, status) =>
            {
                try
                {
                    Assert.False(virtualDevice.Started);
                    Assert.False(physicalDevice.Started);
                    Assert.Equal(CaptureStoppedEventStatus.ErrorWhileCapturing, status);
                    Assert.Equal("capture failed", device.LastError);
                    device.StartCapture();
                    recovered.SetResult();
                }
                catch (Exception ex) { recovered.SetException(ex); }
            };
            device.StartCapture();
            await failingDevice.FailOnCaptureThread();
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(device.Started);
            Assert.Equal(watchVirtualTraffic == VirtualTraffic.External ? 0 : 2, virtualDevice.Starts);
            Assert.Equal(watchVirtualTraffic is VirtualTraffic.External or Both ? 2 : 0, physicalDevice.Starts);
            Assert.Equal(watchVirtualTraffic == VirtualTraffic.External ? 0 : 1, virtualDevice.PacketSubscriptions);
            Assert.Equal(watchVirtualTraffic is VirtualTraffic.External or Both ? 1 : 0, physicalDevice.PacketSubscriptions);
        }

        [Fact]
        public void PhysicalOnlyModeForwardsAllPhysicalTrafficAndSendsOnlyThroughPhysicalDevice()
        {
            var virtualDevice = new CaptureDevice { FailStart = true };
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            device.WatchVirtualTraffic = VirtualTraffic.External;
            var received = 0;
            device.OnPacketArrival += (sender, capture) =>
            {
                Assert.Same(device, sender);
                Assert.Same(device, capture.Device);
                received++;
            };
            device.StartCapture();
            Assert.False(virtualDevice.Started);
            Assert.Equal(0, virtualDevice.PacketSubscriptions);
            Assert.Equal(0, virtualDevice.StopSubscriptions);
            Assert.True(physicalDevice.Started);

            byte[][] packets =
            [
                Frame(Host, VM), Frame(VM, Host), Frame(Network, VM), Frame(VM, Network),
                Frame(Network, Host), Frame(Host, Network), Frame(Network, Broadcast),
                Frame(Network, Multicast), Frame(VM, OtherVM)
            ];
            var header = new Header();
            foreach (var packet in packets)
            {
                var before = received;
                virtualDevice.Receive(packet);
                Assert.Equal(before, received);
                physicalDevice.Receive(packet);
                Assert.Equal(before + 1, received);
                device.SendPacket(packet, header);
            }
            // No MAC filtering, including frames too short for the composite's classifier.
            physicalDevice.Receive(new byte[10]);
            Assert.Equal(packets.Length + 1, received);
            Assert.Empty(virtualDevice.Sent);
            Assert.Equal(packets.Length, physicalDevice.Sent.Count);
            Assert.Same(header, physicalDevice.LastSentHeader);
        }

        [Theory]
        [InlineData(VirtualTraffic.None)]
        [InlineData(VirtualTraffic.Internal)]
        [InlineData(VirtualTraffic.External)]
        [InlineData(Both)]
        public void CanToggleBetweenCaptureRunsWithoutReopening(VirtualTraffic initialMode)
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            var received = 0;
            device.OnPacketArrival += (sender, capture) => received++;
            var packet = Frame(Host, VM);
            var expectedVirtualStarts = 0;
            var expectedPhysicalStarts = 0;
            foreach (var watchVirtualTraffic in new[] { initialMode }.Concat(Modes).Append(initialMode))
            {
                device.WatchVirtualTraffic = watchVirtualTraffic;
                device.StartCapture();
                var captureVirtual = watchVirtualTraffic != VirtualTraffic.External;
                var capturePhysical = watchVirtualTraffic is VirtualTraffic.External or Both;
                var internalTraffic = watchVirtualTraffic is VirtualTraffic.Internal or Both;
                if (captureVirtual) expectedVirtualStarts++;
                if (capturePhysical) expectedPhysicalStarts++;
                Assert.Equal(captureVirtual, virtualDevice.Started);
                Assert.Equal(capturePhysical, physicalDevice.Started);
                Assert.Throws<InvalidOperationException>(() => device.WatchVirtualTraffic =
                    watchVirtualTraffic == VirtualTraffic.None ? Both : VirtualTraffic.None);
                Assert.Equal(watchVirtualTraffic, device.WatchVirtualTraffic);

                var before = received;
                virtualDevice.Receive(packet);
                Assert.Equal(before + (internalTraffic ? 1 : 0), received);
                physicalDevice.Receive(packet);
                Assert.Equal(before + (watchVirtualTraffic == VirtualTraffic.None ? 0 : 1), received);
                virtualDevice.Sent.Clear();
                physicalDevice.Sent.Clear();
                device.SendPacket(packet);
                Assert.Equal(internalTraffic ? 1 : 0, virtualDevice.Sent.Count);
                Assert.Equal(internalTraffic ? 0 : 1, physicalDevice.Sent.Count);

                device.StopCapture();
                Assert.False(virtualDevice.Started);
                Assert.False(physicalDevice.Started);
                Assert.Equal(0, virtualDevice.PacketSubscriptions);
                Assert.Equal(0, physicalDevice.PacketSubscriptions);
            }
            Assert.Single(virtualDevice.OpenModes);
            Assert.Single(physicalDevice.OpenModes);
            Assert.Equal(0, virtualDevice.Closes);
            Assert.Equal(0, physicalDevice.Closes);
            Assert.Equal(expectedVirtualStarts, virtualDevice.Starts);
            Assert.Equal(expectedPhysicalStarts, physicalDevice.Starts);
        }

        [Fact]
        public async Task RetainsEarlyStopUntilConsumerSubscribes()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice();
            using var device = Create(virtualDevice, physicalDevice);
            device.StartCapture();
            await virtualDevice.FailOnCaptureThread();
            Assert.True(SpinWait.SpinUntil(() => !device.Started, TimeSpan.FromSeconds(5)));
            var notified = 0;
            device.OnCaptureStopped += (sender, status) => notified++;
            Assert.Equal(1, notified);
            device.OnCaptureStopped += (sender, status) => notified++;
            Assert.Equal(1, notified);
        }

        [Fact]
        public async Task WorksWithNetworkDeviceIncludingFlagFallbackAndCaptureRecovery()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice
            {
                Name = "physical",
                Description = "Physical adapter",
                MacAddress = PhysicalAddress.Parse(Network),
                RejectNoCaptureLocal = true
            };
            using var composite = Create(virtualDevice, physicalDevice, open: false);
            using var networkDevice = new NetworkDevice(NullLogger<NetworkDevice>.Instance, new HostInterface(), composite);
            Assert.Equal(physicalDevice.Name, composite.Name);
            Assert.Equal(physicalDevice.Description, networkDevice.Name);
            Assert.Equal(physicalDevice.MacAddress, composite.MacAddress);
            Assert.Equal(PhysicalAddress.Parse(Host), networkDevice.PhysicalAddress);
            Assert.False(networkDevice.IsNoCaptureLocal);
            Assert.True(networkDevice.IsMaxResponsiveness);
            Assert.Equal(2, virtualDevice.OpenModes.Count);
            Assert.Equal(virtualDevice.OpenModes, physicalDevice.OpenModes);

            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedCount = 0;
            networkDevice.PacketCaptured += (sender, packet) =>
            {
                if (Interlocked.Increment(ref receivedCount) == 2)
                    received.SetResult();
            };
            networkDevice.StartCapture();
            var local = Frame(Host, VM);
            virtualDevice.Receive(local);
            physicalDevice.Receive(local);

            await physicalDevice.FailOnCaptureThread();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref physicalDevice.Starts) == 2, TimeSpan.FromSeconds(5)));
            var external = Frame(Network, VM);
            virtualDevice.Receive(external);
            physicalDevice.Receive(external);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            networkDevice.StopCapture();
            Assert.Equal(2, receivedCount);
            Assert.False(networkDevice.IsCapturing);
            Assert.Equal(0, virtualDevice.PacketSubscriptions);
            Assert.Equal(0, physicalDevice.PacketSubscriptions);
        }

        [Fact]
        public void RejectsNonEthernetCaptureAndClosesBothDevices()
        {
            var virtualDevice = new CaptureDevice();
            var physicalDevice = new CaptureDevice { LinkType = LinkLayers.Raw };
            using var device = Create(virtualDevice, physicalDevice, open: false);
            Assert.Throws<NotSupportedException>(() => device.Open(new DeviceConfiguration()));
            Assert.False(virtualDevice.Opened);
            Assert.False(physicalDevice.Opened);
        }

        private static HyperVDevice Create(CaptureDevice virtualDevice, CaptureDevice physicalDevice, bool open = true)
        {
            var device = new HyperVDevice(NullLogger<HyperVDevice>.Instance, virtualDevice, physicalDevice,
                PhysicalAddress.Parse(Host), [PhysicalAddress.Parse(VM), PhysicalAddress.Parse(OtherVM)]);
            if (open)
                device.Open(new DeviceConfiguration());
            return device;
        }

        private static byte[] Frame(string source, string destination)
        {
            var frame = new byte[64];
            Convert.FromHexString(destination).CopyTo(frame, 0);
            Convert.FromHexString(source).CopyTo(frame, 6);
            frame[12] = 0x88;
            frame[13] = 0xb5; // Experimental EtherType; no network-layer payload needed.
            return frame;
        }

        private sealed class Header : ICaptureHeader
        {
            public PosixTimeval Timeval { get; } = new();
        }

        private sealed class CaptureDevice : ILiveDevice
        {
            private readonly Header _header = new();
            private Task? _captureThread;
            public string Name { get; init; } = "fake";
            public string Description { get; init; } = "Fake capture device";
            public string? LastError { get; set; }
            public PhysicalAddress MacAddress { get; init; } = PhysicalAddress.Parse(Host);
            public LinkLayers LinkType { get; set; } = LinkLayers.Ethernet;
            public TimestampResolution TimestampResolution => TimestampResolution.Microsecond;
            public ICaptureStatistics? Statistics => null;
            public TimeSpan StopCaptureTimeout { get; set; } = TimeSpan.FromSeconds(5);
            public bool Started { get; private set; }
            internal bool Opened;
            internal bool FailOpen;
            internal bool RejectNoCaptureLocal;
            internal bool FailStart;
            internal string? RejectedFilter;
            internal int Starts;
            internal int Closes;
            internal List<DeviceModes> OpenModes = [];
            internal List<byte[]> Sent = [];
            internal ICaptureHeader? LastSentHeader;
            public string? Filter
            {
                get;
                set
                {
                    if (value is not null && value == RejectedFilter)
                        throw new InvalidOperationException("filter failed");
                    field = value;
                }
            }
            public event PacketArrivalEventHandler? OnPacketArrival;
            public event CaptureStoppedEventHandler? OnCaptureStopped;
            internal int PacketSubscriptions => OnPacketArrival?.GetInvocationList().Length ?? 0;
            internal int StopSubscriptions => OnCaptureStopped?.GetInvocationList().Length ?? 0;

            public void Open(DeviceConfiguration configuration)
            {
                Opened = true; // Deliberately fail after acquiring a resource.
                OpenModes.Add(configuration.Mode);
                if (RejectNoCaptureLocal && configuration.Mode.HasFlag(DeviceModes.NoCaptureLocal))
                    throw (PcapException)Activator.CreateInstance(typeof(PcapException), BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null, args: ["NoCaptureLocal unavailable"], culture: null)!;
                if (FailOpen)
                    throw new InvalidOperationException("open failed");
            }
            public void StartCapture()
            {
                Started = true;
                Starts++;
                if (FailStart)
                    throw new InvalidOperationException("start failed");
            }
            public void StopCapture()
            {
                if (!Started)
                    return;
                if (_captureThread is { } thread)
                {
                    Assert.NotEqual(thread.Id, Task.CurrentId);
                    Assert.True(thread.Wait(StopCaptureTimeout));
                    _captureThread = null;
                }
                Started = false;
                OnCaptureStopped?.Invoke(this, CaptureStoppedEventStatus.CompletedWithoutError);
            }
            internal Task FailOnCaptureThread()
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var captureThread = Task.Run(async () =>
                {
                    await ready.Task;
                    OnCaptureStopped?.Invoke(this, CaptureStoppedEventStatus.ErrorWhileCapturing);
                });
                _captureThread = captureThread;
                ready.SetResult();
                return captureThread;
            }
            internal void Receive(byte[] data) => OnPacketArrival?.Invoke(this, new PacketCapture(this, _header, data));
            public void SendPacket(ReadOnlySpan<byte> packet, ICaptureHeader? header = null)
            {
                Sent.Add(packet.ToArray());
                LastSentHeader = header;
            }
            public void Capture() => throw new NotSupportedException();
            public GetPacketStatus GetNextPacket(out PacketCapture capture) => throw new NotSupportedException();
            public void Close()
            {
                StopCapture();
                Opened = false;
                Closes++;
                OnPacketArrival = null;
            }
            public void Dispose() => Close();
        }

        private sealed class HostInterface : INetworkInterface
        {
            public NetworkIdentity Identity => new("host");
            public string Name => "Host";
            public OperationalStatus Status => OperationalStatus.Up;
            public NetworkInterfaceType Type => NetworkInterfaceType.Ethernet;
            public PhysicalAddress PhysicalAddress => PhysicalAddress.Parse(Host);
            public IReadOnlyList<UnicastIPAddressInformation> Addresses => [];
            public IReadOnlyList<GatewayIPAddressInformation> Gateways => [];
            public string? DNSSuffix => null;
            public int? IPv6ScopeIndex => null;
            public string? SSID => null;
            public bool IsDisabled => false;
            public bool? ShouldBeDisabled { get; set; }
        }
    }
}
