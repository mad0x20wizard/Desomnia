using PacketDotNet;
using SharpPcap;
using System.Net.NetworkInformation;

namespace CaptureAllocation.Benchmarks;

// Only supplies the device metadata used by the REAL PacketCapture.GetPacket implementation.
// No Npcap installation, NIC, file I/O, reflection dispatch, or mock-framework allocation.
internal sealed class SyntheticDevice : ICaptureDevice
{
    public LinkLayers LinkType => LinkLayers.Ethernet;
    public string Name => "synthetic-ethernet";
    public string Description => Name;
    public string? LastError => null;
    public string? Filter { get; set; }
    public PhysicalAddress? MacAddress => null;
    public bool Started => false;
    public TimeSpan StopCaptureTimeout { get; set; }
    public ICaptureStatistics? Statistics => null;
    public TimestampResolution TimestampResolution => TimestampResolution.Microsecond;
    public event PacketArrivalEventHandler OnPacketArrival { add { } remove { } }
    public event CaptureStoppedEventHandler OnCaptureStopped { add { } remove { } }
    public void Open(DeviceConfiguration configuration) => throw new NotSupportedException();
    public void Close() { }
    public void Dispose() { }
    public void StartCapture() => throw new NotSupportedException();
    public void StopCapture() => throw new NotSupportedException();
    public void Capture() => throw new NotSupportedException();
    public GetPacketStatus GetNextPacket(out PacketCapture capture) => throw new NotSupportedException();
}

internal sealed class SyntheticHeader : ICaptureHeader
{
    // Model timestamp materialization, without a wall-clock read on every packet.
    // Creation/marshalling of the native capture header is deliberately outside this test.
    public PosixTimeval Timeval => new(1_700_000_000, 0);
}
