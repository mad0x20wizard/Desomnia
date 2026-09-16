using Autofac;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network
{
    public class NetworkDevice : IDisposable
    {
        const int PacketQueueCapacity = 4096; // ~ 6 MB of raw data

        static readonly TimeSpan ProcessingStopTimeout = TimeSpan.FromSeconds(10);

        public ILogger<NetworkDevice> Logger { private get; init; }

        public  string              Name => Device.Description ?? Device.Name;
        public  INetworkInterface   Interface   { get; }
        private ILiveDevice         Device      { get; init; }

        public bool IsCapturing => Device.Started;

        public bool IsMaxResponsiveness;
        public bool IsNoCaptureLocal;

        public string? Filter
        {
            get => Device.Filter;

            set => Device.Filter = value;
        }

        public IEnumerable<IDevicePacketFilter> Filters { private get; init; } = [];

        public PhysicalAddress PhysicalAddress => Interface.PhysicalAddress;

        public IEnumerable<IPAddress> IPAddresses
        {
            get
            {
                IEnumerable<IPAddress> pcapAddresses = [];

                if (Device is LibPcapLiveDevice pcap)
                {
                    pcapAddresses = pcap.Addresses
                        .Where(address => address.Addr?.ipAddress is not null)
                        .Select(address => address.Addr?.ipAddress!);
                }

                var niAddresses = Interface.Addresses.Select(unicast => unicast.Address);

                return pcapAddresses.Concat(niAddresses).Select(IPAddressExt.RemoveScopeId).Distinct();
            }
        }

        public IPAddress? IPv4Address => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault();
        public IPAddress? IPv6LinkLocalAddress => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal).FirstOrDefault();
        public IEnumerable<IPAddress> IPv6Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);

        public IPAddress? IPv6LinkLocalMulticastAddress
        {
            get
            {
                if (IPv6Addresses.Any() && Interface.IPv6ScopeIndex is int scopeId)
                {
                    return new IPAddress(IPAddressExt.LinkLocalMulticast.GetAddressBytes(), scopeId);
                }

                return null;
            }
        }

        public event EventHandler<EthernetPacket>?  PacketCaptured;
        public event EventHandler<PacketCapture>?   PacketDropped;

        private PacketCaptureContext? _context;

        private bool _shouldBeCapturing;

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

        public bool HasSentPacket(EthernetPacket packet) => HasSentPacket(packet.SourceHardwareAddress);

        // TODO will this work with virtual interfaces? (OpenVPN)
        public bool HasSentPacket(PhysicalAddress? source) => source is not null && PhysicalAddress.Equals(source); 

        public void StartCapture()
        {
            if (IsCapturing)
                return;

            if (!_shouldBeCapturing)
            {
                _context = new PacketCaptureContext(Name, PacketQueueCapacity);
                _context.StartProcessing(ProcessQueuedPackets);

                _shouldBeCapturing = true;
            }

            try
            {
                Device.OnPacketArrival += Device_OnPacketArrival;
                Device.StartCapture();
                Device.OnCaptureStopped += Device_OnCaptureStopped;
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

            if (Filter != null)
            {
                Logger.LogDebug("BPF rule = '{expr}'", Filter);
            }
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

            Device.OnCaptureStopped -= Device_OnCaptureStopped;
            Device.OnPacketArrival -= Device_OnPacketArrival;

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

            StartCapture();
        }

        /// <summary>
        /// The single consumer: drains the user-space buffer and dispatches each packet in arrival
        /// order via <see cref="PacketCaptured"/>. Running off the capture thread means a slow
        /// handler no longer stalls capture or overflows the kernel ring buffer.
        /// </summary>
        private void ProcessQueuedPackets(PacketCaptureContext ctx)
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
                        Device.SendPacket(packet);
                    }
                }
            }
            catch (DeviceNotReadyException ex)
            {
                Logger.LogWarning(ex, "");
            }
        }

        private async Task<bool> UntilFullyOperational()
        {
            const int MAX_RETRIES = 16;
            const int WAIT_TIME = 500;

            int retry = 0;

            while (true)
            {
                try
                {
                    lock (Device)
                    {
                        Device.SendPacket(new EthernetPacket(PhysicalAddressExt.Empty, PhysicalAddressExt.Empty, EthernetType.WakeOnLan)
                        {
                            PayloadPacket = new WakeOnLanPacket(PhysicalAddressExt.Empty)
                        });
                    }

                    return true;
                }
                catch (PcapException ex)
                {
                    if (retry++ == 0)
                    {
                        Logger.LogTrace($"Network device \"{Name}\" is not yet fully operational. Waiting up to {MAX_RETRIES * WAIT_TIME / 1000} seconds...");
                    }
                    else if (retry >= MAX_RETRIES)
                    {
                        Logger.LogError(ex, $"Network interface \"{Name}\" has not become fully operational.");

                        return false;
                    }

                    await Task.Delay(WAIT_TIME);
                }
            }
        }

        internal void Restart()
        {
            StopCapture();

            var filter = Filter;

            lock (Device)
            {
                Device.Close();
            }

            TryOpen(Device, ref IsMaxResponsiveness, ref IsNoCaptureLocal);

            Filter = filter;

            StartCapture();
        }

        public void StopCapture()
        {
            _shouldBeCapturing = false;

            if (IsCapturing)
            {
                Device.StopCapture();
            }

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
