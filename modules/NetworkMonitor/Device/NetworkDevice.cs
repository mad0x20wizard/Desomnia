using Autofac;
using MadWizard.Desomnia.Extensions.EventHandler;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Device
{
    public class NetworkDevice : IDisposable
    {
        const int PacketQueueCapacity = 4096; // ~ 6 MB of raw data

        static readonly TimeSpan ProcessingStopTimeout = TimeSpan.FromSeconds(10);

        public ILogger<NetworkDevice> Logger { private get; init; }

        public  string              Name => Device.Description ?? Device.Name;

        internal ILiveDevice        Device      { get; }
        public INetworkInterface    Interface   { get; }

        public bool IsCapturing => Device.Started;

        public bool IsMaxResponsiveness;
        public bool IsNoCaptureLocal;

        public IEnumerable<IDevicePacketFilter> Filters { private get; init; } = [];

        public PhysicalAddress PhysicalAddress => Interface.PhysicalAddress;

        public IEnumerable<IPAddress> IPAddresses
        {
            get
            {
                return Interface.Addresses.Select(unicast => unicast.Address).Select(IPAddressExt.RemoveScopeId).Distinct();
            }
        }

        public IPAddress? IPv4Address => IPAddresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        public IPAddress? IPv6LinkLocalAddress => IPAddresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal);
        public IEnumerable<IPAddress> IPv6Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);

        public IPAddress? IPv6LinkLocalMulticastAddress
        {
            get
            {
                if (IPv6Addresses.Any() && Interface.IPv6ScopeIndex is int scopeId)
                {
                    return new IPAddress(IPAddress.LinkLocalMulticast.GetAddressBytes(), scopeId);
                }

                return null;
            }
        }

        /// <summary>Raised before native capture starts. A handler may abort startup by throwing.</summary>
        public event EventHandler? CaptureStarting;

        public event EventHandler<EthernetPacket>? PacketCaptured;
        public event ReadonlyEventHandler<PacketCapture>? PacketDropped;

        /// <summary>Raised after capture stops, including failed startup, before any automatic restart.</summary>
        public event EventHandler? CaptureStopped;

        private PacketCaptureQueue? _context;

        private bool _shouldBeCapturing;
        private bool _shouldNotifyCaptureStopped;

        public NetworkDevice(ILogger<NetworkDevice> logger, INetworkInterface @interface, ILiveDevice device)
        {
            Logger = logger;

            Interface = @interface;
            Device = device;

            if (!TryOpen(Device, ref IsMaxResponsiveness, ref IsNoCaptureLocal))
            {
                throw new Exception($"Failed to open network device \"{Name}\"");
            }
        }

        public bool HasSentPacket(INetworkCapture packet) => HasSentPacket(packet.SourcePhysicalAddress);

        // TODO will this work with virtual interfaces? (OpenVPN)
        public bool HasSentPacket(PhysicalAddress? source) => source is not null && PhysicalAddress.Equals(source); 

        public void StartCapture()
        {
            if (IsCapturing)
                return;

            if (!_shouldBeCapturing)
            {
                _context = new PacketCaptureQueue(Name, PacketQueueCapacity);
                _context.StartProcessing(ProcessQueuedPackets);

                _shouldBeCapturing = true;
            }

            try
            {
                _shouldNotifyCaptureStopped = true;
                CaptureStarting?.Invoke(this, EventArgs.Empty);
                Device.OnPacketArrival += Device_OnPacketArrival;
                Device.OnCaptureStopped += Device_OnCaptureStopped;
                Device.StartCapture();
            }
            catch
            {
                StopCapture();

                throw;
            }

            List<string> features = [];
            if (IsMaxResponsiveness)
                features.Add("MaxResponsiveness");
            if (IsNoCaptureLocal)
                features.Add("NoCaptureLocal");

            var countIPv6 = IPv6Addresses.Count();

            Logger.LogInformation($"Capturing network device \"{Name}\"; MAC={PhysicalAddress?.ToHexString()}, IPv4={IPv4Address?.ToString() ?? "?"}" +
                (countIPv6 > 0 ? $", IPv6={IPv6LinkLocalAddress?.ToString() ?? IPv6Addresses.FirstOrDefault()?.ToString() ?? "?"}" + (countIPv6 - 1 > 0 ? $"(+{countIPv6 - 1})" : "") : "") +
                $" [{string.Join(", ", features)}]");
        }

        /// <summary>
        /// Capture-thread callback. To respect libpcap's single-threaded handle requirement and to
        /// keep draining the kernel buffer, this does the bare minimum on the capture thread: filter
        /// out our own injected packets, reserve bounded queue capacity, then copy accepted bytes out
        /// of the libpcap-owned buffer. Parsing and dispatch happen later on the processing thread.
        /// </summary>
        private void Device_OnPacketArrival(object sender, PacketCapture capture)
        {
            try
            {
                if (!FilterInjectedPacket(capture) && _context is not null)
                {
                    try
                    {
                        if (!_context.EnqueueCapture(capture))
                        {
                            PacketDropped?.Invoke(this, capture);
                        }
                    }
                    catch (CapturingStoppedException)
                    {
                        // The queue was completed concurrently during shutdown; nothing to do.
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while filtering/queuing packet."); // low level error
            }
        }

        private void Device_OnCaptureStopped(object sender, CaptureStoppedEventStatus status)
        {
            string message = $"Stopped capturing network device \"{Name}\"";

            NotifyCaptureStopped();

            switch (status)
            {
                case CaptureStoppedEventStatus.CompletedWithoutError when _shouldBeCapturing == false:
                    Logger.LogInformation(message);
                    return;

                case CaptureStoppedEventStatus.CompletedWithoutError when _shouldBeCapturing:
                    Logger.LogWarning(message + " – restarting...");
                    break;

                case CaptureStoppedEventStatus.ErrorWhileCapturing:
                    if (Device.LastError is string error)
                        message += " – " + error;
                    Logger.LogError(message);
                    break;
            }

            if (_shouldBeCapturing)
                StartCapture();
        }

        private void NotifyCaptureStopped()
        {
            Device.OnCaptureStopped -= Device_OnCaptureStopped;
            Device.OnPacketArrival  -= Device_OnPacketArrival;

            if (_shouldNotifyCaptureStopped)
            {
                _shouldNotifyCaptureStopped = false;

                CaptureStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// The single consumer: drains the user-space buffer and dispatches each packet in arrival
        /// order via <see cref="PacketCaptured"/>. Running off the capture thread means a slow
        /// handler no longer stalls capture or overflows the kernel ring buffer.
        /// </summary>
        private void ProcessQueuedPackets(PacketCaptureQueue ctx)
        {
            try
            {
                foreach (var raw in ctx)
                {
                    try
                    {
                        if (Packet.ParsePacket(raw.LinkLayerType, raw.Data) is EthernetPacket ethernet)
                        {
                            try
                            {
                                PacketCaptured?.Invoke(this, ethernet);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, "Error processing packet:\n{packet}", ethernet.ToTraceString());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error parsing packet.");
                    }
                }
            }
            catch (CapturingStoppedException)
            {
                // Queue was disposed/completed while we were blocked in GetConsumingEnumerable; exit.
            }
        }

        private bool FilterInjectedPacket(PacketCapture capture)
        {
            foreach (var filter in Filters)
            {
                if (filter.FilterIncoming(capture))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PreparePacketToSend(EthernetPacket packet)
        {
            if (packet.Extract<UdpPacket>() is UdpPacket udp)
            {
                udp.UpdateCalculatedValues();
                udp.UpdateUdpChecksum();
            }

            if (packet.Extract<IPPacket>() is IPPacket ip)
            {
                ip.UpdateCalculatedValues();

                if (ip is IPv4Packet ipv4Packet)
                    ipv4Packet.UpdateIPChecksum();
            }
        }

        /// <summary>Sends stored frame bytes; use prepare to update generated IP/UDP headers first.</summary>
        public void SendPacket(EthernetPacket packet, bool prepare = false)
        {
            if (prepare)
            {
                PreparePacketToSend(packet);
            }

            try
            {
                if (!Filters.Select(filter => filter.FilterOutgoing(packet)).Where(f => f == true).Any())
                {
                    if (Logger.IsEnabled(LogLevel.Trace))
                    {
                        Logger.LogTrace($"SEND PACKET\n{packet.ToTraceString()}");
                    }

                    lock (Device)
                    {
                        Device.SendPacket(packet.CapturedBytes);
                    }
                }
            }
            catch (DeviceNotReadyException ex)
            {
                Logger.LogWarning(ex, "");
            }
        }

        internal void Restart()
        {
            StopCapture();

            lock (Device)
            {
                Device.Close();
            }

            if (!TryOpen(Device, ref IsMaxResponsiveness, ref IsNoCaptureLocal))
                throw new InvalidOperationException($"Failed to reopen network device '{Name}'.");

            StartCapture();
        }

        public void StopCapture()
        {
            _shouldBeCapturing = false;

            if (IsCapturing)
            {
                Device.StopCapture();
            }

            NotifyCaptureStopped();

            _context?.StopProcessing(ProcessingStopTimeout);
            _context?.Dispose();
            _context = null;
        }

        private bool TryOpen(ILiveDevice device, ref bool maxResponsiveness, ref bool noCaptureLocal)
        {
            try
            {
                device.Open(DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness | DeviceModes.NoCaptureLocal);

                maxResponsiveness = true;
                noCaptureLocal = true;

                return true;
            }
            catch (PcapException)
            {
                Logger.LogDebug($"Device '{Name}' does not support NoCaptureLocal mode. Compensating with fallback buffer.");
            }

            noCaptureLocal = false; // not supported

            try
            {
                device.Open(DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness);

                maxResponsiveness = true;

                return true;
            }
            catch (PcapException)
            {
                Logger.LogWarning($"Device '{Name}' does not support MaxResponsiveness mode. Anticipate slow application behavior.");
            }

            maxResponsiveness = false; // not supported

            try
            {
                device.Open(DeviceModes.Promiscuous);

                return true;
            }
            catch (PcapException)
            {
                Logger.LogError($"Device '{Name}' does not support Promiscuous mode.");
            }

            return false; // at least promiscuous mode is needed
        }

        void IDisposable.Dispose()
        {
            StopCapture();

            lock (Device)
            {
                Device.Close();
            }
        }
    }
}
