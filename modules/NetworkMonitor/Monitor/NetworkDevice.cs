using Autofac;
using MadWizard.Desomnia.Network.Filter;
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
        public ILogger<NetworkDevice> Logger { private get; init; }

        public  string              Name => Device.Description ?? Device.Name;
        public  NetworkInterface    Interface   { get; internal set; }
        private ILiveDevice         Device      { get; init; }

        public bool IsCapturing => Device.Started;

        public bool IsMaxResponsiveness;
        public bool IsNoCaptureLocal;

        public string? Filter
        {
            get => Device.Filter;

            set
            {
                if (Device.Filter != value)
                {
                    var runtime = Device.Started;

                    if (runtime)
                    {
                        Logger.LogDebug("BPF rule = '{expr}'", value);

                        Device.StopCapture();
                    }

                    Device.Filter = value;

                    if (runtime)
                    {
                        Device.StartCapture();
                    }
                }
            }
        }
        public IEnumerable<IDevicePacketFilter> Filters { private get; init; } = [];

        public PhysicalAddress PhysicalAddress => Interface.GetPhysicalAddress() ?? PhysicalAddress.None;

        public IEnumerable<IPAddress> IPAddresses
        {
            get
            {
                IEnumerable<IPAddress> pcapAddresses = [];
                IEnumerable<IPAddress> niAddresses = [];

                if (Device is LibPcapLiveDevice pcap)
                {
                    pcapAddresses = pcap.Addresses
                        .Where(address => address.Addr?.ipAddress is not null)
                        .Select(address => address.Addr?.ipAddress!);
                }

                niAddresses = Interface.GetIPProperties().UnicastAddresses
                    .Where(unicast => unicast.Address is not null)
                    .Select(unicast => unicast.Address);

                return pcapAddresses.Concat(niAddresses).Select(IPAddressExt.RemoveScopeId).Distinct();
            }
        }

        public IPAddress? IPv4Address => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault();
        public IPAddress? IPv6LinkLocalAddress => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal).FirstOrDefault();
        public IEnumerable<IPAddress> IPv6Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);

        public event EventHandler<EthernetPacket>? EthernetCaptured;

        public NetworkDevice(ILogger<NetworkDevice> logger, NetworkInterface @interface, ILiveDevice device)
        {
            Logger = logger;

            Interface = @interface;
            Device = device;

            if (!TryOpen(Device, ref IsMaxResponsiveness, ref IsNoCaptureLocal))
            {
                throw new Exception($"Failed to open network device \"{Name}\"");
            }
        }

        public bool HasSentPacket(EthernetPacket packet)
        {
            return this.PhysicalAddress.Equals(packet.SourceHardwareAddress); // TODO will this work with virtual interfaces? (OpenVPN)
        }

        public void StartCapture()
        {
            Device.OnPacketArrival += Device_OnPacketArrival;
            Device.StartCapture();

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

        private void Device_OnPacketArrival(object sender, PacketCapture capture)
        {
            try
            {
                if (!FilterInjectedPacket(capture))
                {
                    var raw = capture.GetPacket();

                    if (Packet.ParsePacket(raw.LinkLayerType, raw.Data) is EthernetPacket ethernet)
                    {
                        if (Logger.IsEnabled(LogLevel.Trace)) 
                        {
                            //Logger.LogTrace($"RECEIVED PACKET\n{ethernet.ToTraceString()}");
                        }

                        try
                        {
                            EthernetCaptured?.Invoke(this, ethernet);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error processing packet:\n{packet}", ethernet.ToTraceString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while filtering/parsing packet."); // low level error
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

        public void SendPacket(EthernetPacket packet)
        {
            if (!Filters.Select(filter => filter.FilterOutgoing(packet)).Where(f => f == true).Any())
            {
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.LogTrace($"SEND PACKET\n{packet.ToTraceString()}");
                }

                Device.SendPacket(packet);
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
                    Device.SendPacket(new EthernetPacket(PhysicalAddressExt.Empty, PhysicalAddressExt.Empty, EthernetType.WakeOnLan)
                    {
                        PayloadPacket = new WakeOnLanPacket(PhysicalAddressExt.Empty)
                    });

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

        public void StopCapture()
        {
            if (Device.Started)
            {
                Device.StopCapture();

                Logger.LogInformation($"Stopped capturing network device \"{Name}\"");
            }
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
            if (IsCapturing)
            {
                StopCapture();
            }

            Device.Close();
        }
    }

    /// <summary>
    /// This Filter prevent packets sent by us, being processed as incoming packets again,
    /// if the device is cannot do this by itself.
    /// </summary>
    /// 
    /// <param name="device">the monitored network device</param>
    internal class LocalPacketFilter(NetworkDevice device) : IDevicePacketFilter
    {
        private readonly IList<byte[]> _sentPackets = [];

        public bool FilterIncoming(PacketCapture packet)
        {
            if (device.IsNoCaptureLocal)
                return false;

            lock (_sentPackets)
            {
                foreach (var bytes in _sentPackets)
                    if (packet.Data.SequenceEqual(bytes))
                        return _sentPackets.Remove(bytes);
            }

            return false;
        }

        public bool FilterOutgoing(Packet packet)
        {
            if (device.IsNoCaptureLocal)
                return false;

            lock (_sentPackets)
            {
                _sentPackets.Add(packet.Bytes);
            }

            return false;
        }
    }

    internal class SimulationPacketFilter : IDevicePacketFilter
    {
        public required ILogger<SimulationPacketFilter> Logger { private get; init; }

        public bool FilterIncoming(PacketCapture packet) => false;

        public bool FilterOutgoing(Packet packet) => true;
    }
}
