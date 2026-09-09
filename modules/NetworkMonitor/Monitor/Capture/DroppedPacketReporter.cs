using Microsoft.Extensions.Logging;
using SharpPcap;

namespace MadWizard.Desomnia.Network
{
    internal class DroppedPacketReporter
    {
        public required ILogger<DroppedPacketReporter> Logger { private get; init; }

        public required TimeSpan DropWarningInterval { private get; init; }

        private string _name;

        private long _droppedPacketCount;
        private long _droppedByteCount;
        private int _droppedSinceWarning;
        private long _lastDropWarningAt;

        public long DroppedPacketCount => Interlocked.Read(ref _droppedPacketCount);
        public long DroppedByteCount => Interlocked.Read(ref _droppedByteCount);

        public DroppedPacketReporter(NetworkDevice device)
        {
            _name = device.Name;

            device.PacketDropped += Device_PacketDropped;
        }

        private void Device_PacketDropped(object? sender, PacketCapture capture)
        {
            int bytes = capture.Data.Length;

            Interlocked.Increment(ref _droppedPacketCount);
            Interlocked.Add(ref _droppedByteCount, bytes);
            Interlocked.Increment(ref _droppedSinceWarning);

            long now = Environment.TickCount64;
            long previous = Volatile.Read(ref _lastDropWarningAt);

            if (now - previous >= DropWarningInterval.TotalMilliseconds
                && Interlocked.CompareExchange(ref _lastDropWarningAt, now, previous) == previous)
            {
                int dropped = Interlocked.Exchange(ref _droppedSinceWarning, 0);

                Logger.LogWarning(
                    "Processing queue for \"{Name}\" is saturated; dropped {Count} packet(s) before copying them ({TotalPackets} packets / {TotalBytes} bytes total).",
                    _name, dropped, DroppedPacketCount, DroppedByteCount);
            }
        }
    }
}
