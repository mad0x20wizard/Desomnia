using PacketDotNet;
using SharpPcap;

namespace MadWizard.Desomnia.Network.Device.Filter
{
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
                _sentPackets.Add(packet.CapturedBytes.ToArray());
            }

            return false;
        }
    }
}
