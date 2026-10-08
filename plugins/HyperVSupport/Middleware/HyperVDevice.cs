using MadWizard.Desomnia.Network.HyperV.Configuration;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.HyperV.Middleware
{
    /// <summary>Combines the host and uplink adapters of an external Hyper-V switch.</summary>
    internal sealed class HyperVDevice : ILiveDevice
    {
        private readonly ILiveDevice _virtualDevice;
        private readonly ILiveDevice _physicalDevice;
        private readonly PhysicalAddress _hostAddress;
        private readonly ILogger<HyperVDevice> _logger;
        private readonly object _lifecycle = new();
        private HyperVPacketMap _map;
        private CaptureRun? _run;
        private bool _opened;
        private string? _lastError;
        private CaptureStoppedEventHandler? _captureStopped;
        private CaptureStoppedEventStatus? _pendingStop;

        public HyperVDevice(ILogger<HyperVDevice> logger, ILiveDevice virtualDevice, ILiveDevice physicalDevice,
            PhysicalAddress hostAddress, IEnumerable<PhysicalAddress> virtualMachineAddresses)
        {
            if (ReferenceEquals(virtualDevice, physicalDevice))
                throw new ArgumentException("The virtual and physical capture devices must be different.");
            _logger = logger;
            _virtualDevice = virtualDevice;
            _physicalDevice = physicalDevice;
            _hostAddress = hostAddress;
            _map = new HyperVPacketMap(hostAddress, virtualMachineAddresses);
        }

        public string Name => _physicalDevice.Name;
        public string Description => _physicalDevice.Description;
        public PhysicalAddress? MacAddress => _physicalDevice.MacAddress;
        public LinkLayers LinkType => LinkLayers.Ethernet;
        public TimestampResolution TimestampResolution => _physicalDevice.TimestampResolution;
        public string? LastError => _lastError ?? _physicalDevice.LastError ?? _virtualDevice.LastError;
        public bool Started => Volatile.Read(ref _run) is not null;

        /// <summary>Changes take effect on the next capture run; stop capture before changing this value.</summary>
        public VirtualTraffic WatchVirtualTraffic
        {
            get;
            set
            {
                lock (_lifecycle)
                {
                    if ((value & ~(VirtualTraffic.Internal | VirtualTraffic.External)) != 0)
                        throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown virtual traffic mode.");
                    if (_run is not null && field != value)
                        throw new InvalidOperationException("Stop Hyper-V capture before changing WatchVirtualTraffic.");
                    field = value;
                }
            }
        } = VirtualTraffic.Internal | VirtualTraffic.External;

        public TimeSpan StopCaptureTimeout
        {
            get => _physicalDevice.StopCaptureTimeout;
            set
            {
                lock (_lifecycle)
                {
                    _virtualDevice.StopCaptureTimeout = value;
                    _physicalDevice.StopCaptureTimeout = value;
                }
            }
        }

        public string? Filter
        {
            get;
            set
            {
                lock (_lifecycle)
                {
                    var previous = field;
                    try
                    {
                        _virtualDevice.Filter = value;
                        _physicalDevice.Filter = value;
                        field = value;
                    }
                    catch
                    {
                        try { ApplyToBoth(device => device.Filter = previous); }
                        catch { Close(); }
                        throw;
                    }
                }
            }
        }

        // Driver counters are measured before the composite's MAC filtering.
        public ICaptureStatistics? Statistics
        {
            get
            {
                lock (_lifecycle)
                {
                    if (WatchVirtualTraffic is VirtualTraffic.None or VirtualTraffic.Internal)
                        return _virtualDevice.Statistics;
                    if (WatchVirtualTraffic == VirtualTraffic.External)
                        return _physicalDevice.Statistics;
                    var physicalStatistics = _physicalDevice.Statistics;
                    var virtualStatistics = _virtualDevice.Statistics;
                    return virtualStatistics is null || physicalStatistics is null ? null : new CaptureStatistics
                    {
                        ReceivedPackets = virtualStatistics.ReceivedPackets + physicalStatistics.ReceivedPackets,
                        DroppedPackets = virtualStatistics.DroppedPackets + physicalStatistics.DroppedPackets,
                        InterfaceDroppedPackets = virtualStatistics.InterfaceDroppedPackets + physicalStatistics.InterfaceDroppedPackets
                    };
                }
            }
        }

        public event PacketArrivalEventHandler? OnPacketArrival;
        public event CaptureStoppedEventHandler OnCaptureStopped
        {
            add
            {
                lock (_lifecycle)
                {
                    _captureStopped += value;
                    // NetworkDevice subscribes after StartCapture; retain early failures.
                    if (_pendingStop is { } status)
                        NotifyStopped(status);
                }
            }
            remove { lock (_lifecycle) _captureStopped -= value; }
        }

        internal void UpdateVirtualMachineAddresses(IEnumerable<PhysicalAddress> addresses)
        {
            Volatile.Write(ref _map, new HyperVPacketMap(_hostAddress, addresses));
        }

        public void Open(DeviceConfiguration configuration)
        {
            lock (_lifecycle)
            {
                if (_opened)
                    return;
                try
                {
                    // Keep both handles configured so capture can switch modes without reopening.
                    // NoCaptureLocal, like the other options, must succeed on both.
                    _virtualDevice.Open(configuration);
                    _physicalDevice.Open(configuration);
                    if (_virtualDevice.LinkType != LinkLayers.Ethernet || _physicalDevice.LinkType != LinkLayers.Ethernet)
                        throw new NotSupportedException("Hyper-V capture requires two Ethernet devices.");
                    if (_virtualDevice.TimestampResolution != _physicalDevice.TimestampResolution)
                        throw new NotSupportedException("Hyper-V capture devices have different timestamp resolutions.");
                    _opened = true;
                    _lastError = null;
                }
                catch
                {
                    // A failed Open can already own a handle. Release both before a
                    // retry with fewer flags, including the device whose Open failed.
                    try { ApplyToBoth(device => device.Close()); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not close Hyper-V capture devices after opening failed."); }
                    throw;
                }
            }
        }

        public void StartCapture()
        {
            lock (_lifecycle)
            {
                if (_run is not null)
                    return;
                if (!_opened)
                    throw new InvalidOperationException("The Hyper-V capture device is not open.");
                _pendingStop = null;
                var run = new CaptureRun { WatchVirtualTraffic = WatchVirtualTraffic };
                run.VirtualPacket = (sender, capture) => Receive(run, true, capture);
                run.PhysicalPacket = (sender, capture) => Receive(run, false, capture);
                run.Stopped = (sender, status) => CaptureStopped(run, (ILiveDevice)sender, status);
                if (run.CaptureVirtual)
                {
                    _virtualDevice.OnPacketArrival += run.VirtualPacket;
                    _virtualDevice.OnCaptureStopped += run.Stopped;
                }
                if (run.CapturePhysical)
                {
                    _physicalDevice.OnPacketArrival += run.PhysicalPacket;
                    _physicalDevice.OnCaptureStopped += run.Stopped;
                }
                Volatile.Write(ref _run, run);
                try
                {
                    if (run.CaptureVirtual)
                        _virtualDevice.StartCapture();
                    if (run.CapturePhysical)
                        _physicalDevice.StartCapture();
                }
                catch
                {
                    EndCapture(run);
                    throw;
                }
            }
        }

        private void Receive(CaptureRun run, bool virtualDevice, PacketCapture capture)
        {
            // No packet parsing or copying: the borrowed buffer is valid for this callback.
            if (!run.AcceptPackets)
                return;

            // Single-adapter modes need no MAC filter. None excludes known VM
            // endpoints; combined capture partitions packets between the adapters.
            var accept = run.WatchVirtualTraffic switch
            {
                VirtualTraffic.None => Volatile.Read(ref _map).AcceptNonVirtualTraffic(capture.Data),
                VirtualTraffic.Internal | VirtualTraffic.External => Volatile.Read(ref _map).Accept(capture.Data, virtualDevice),
                _ => true
            };
            if (accept)
                OnPacketArrival?.Invoke(this, new PacketCapture(this, capture.Header, capture.Data));
        }

        private void CaptureStopped(CaptureRun run, ILiveDevice device, CaptureStoppedEventStatus status)
        {
            if (!run.AcceptPackets || Interlocked.Exchange(ref run.StopScheduled, 1) != 0)
                return;
            run.AcceptPackets = false;
            // The event arrives on the capture thread. Join it on another thread
            // before notifying a consumer which may immediately start again.
            _ = Task.Run(() =>
            {
                lock (_lifecycle)
                {
                    if (!ReferenceEquals(_run, run))
                        return;
                    try
                    {
                        _lastError = device.LastError;
                        try { EndCapture(run); }
                        catch (Exception ex)
                        {
                            _lastError = ex.Message;
                            status = CaptureStoppedEventStatus.ErrorWhileCapturing;
                        }
                        NotifyStopped(status);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Could not recover Hyper-V packet capture."); }
                }
            });
        }

        public void StopCapture()
        {
            lock (_lifecycle)
            {
                _pendingStop = null;
                if (_run is not { } run)
                    return;
                EndCapture(run);
                NotifyStopped(CaptureStoppedEventStatus.CompletedWithoutError);
            }
        }

        private void EndCapture(CaptureRun run)
        {
            run.AcceptPackets = false;
            try { ApplyToBoth(device => device.StopCapture()); }
            finally
            {
                _virtualDevice.OnPacketArrival -= run.VirtualPacket;
                _physicalDevice.OnPacketArrival -= run.PhysicalPacket;
                _virtualDevice.OnCaptureStopped -= run.Stopped;
                _physicalDevice.OnCaptureStopped -= run.Stopped;
                Volatile.Write(ref _run, null);
            }
        }

        private void NotifyStopped(CaptureStoppedEventStatus status)
        {
            _pendingStop = _captureStopped is null ? status : null;
            _captureStopped?.Invoke(this, status);
        }

        public void SendPacket(ReadOnlySpan<byte> packet, ICaptureHeader? header = null)
        {
            if (packet.Length < HyperVPacketMap.EthernetHeaderLength)
                throw new ArgumentException("An Ethernet header is required.", nameof(packet));
            lock (_lifecycle)
            {
                var device = (WatchVirtualTraffic & VirtualTraffic.Internal) != 0 && Volatile.Read(ref _map).SendViaVirtualDevice(packet)
                    ? _virtualDevice : _physicalDevice;
                device.SendPacket(packet, header);
            }
        }

        // Desomnia uses event-based capture. Polling two borrowed native buffers would
        // require a separate copying queue and different ownership semantics.
        public void Capture() => throw new NotSupportedException("Use StartCapture for Hyper-V capture.");
        public GetPacketStatus GetNextPacket(out PacketCapture capture) => throw new NotSupportedException("Use OnPacketArrival for Hyper-V capture.");

        public void Close()
        {
            lock (_lifecycle)
            {
                try
                {
                    if (_run is { } run)
                        EndCapture(run);
                }
                finally
                {
                    _opened = false;
                    _pendingStop = null;
                    OnPacketArrival = null;
                    _captureStopped = null;
                    ApplyToBoth(device => device.Close());
                }
            }
        }

        public void Dispose()
        {
            try { Close(); }
            finally { ApplyToBoth(device => device.Dispose()); }
        }

        private void ApplyToBoth(Action<ILiveDevice> action)
        {
            try { action(_virtualDevice); }
            finally { action(_physicalDevice); }
        }

        private sealed class CaptureRun
        {
            internal VirtualTraffic WatchVirtualTraffic { get; init; }
            internal bool CaptureVirtual => WatchVirtualTraffic != VirtualTraffic.External;
            internal bool CapturePhysical => (WatchVirtualTraffic & VirtualTraffic.External) != 0;
            internal volatile bool AcceptPackets = true;
            internal int StopScheduled;
            internal PacketArrivalEventHandler VirtualPacket = null!;
            internal PacketArrivalEventHandler PhysicalPacket = null!;
            internal CaptureStoppedEventHandler Stopped = null!;
        }

        private sealed class CaptureStatistics : ICaptureStatistics
        {
            public uint ReceivedPackets { get; set; }
            public uint DroppedPackets { get; set; }
            public uint InterfaceDroppedPackets { get; set; }
        }
    }
}
