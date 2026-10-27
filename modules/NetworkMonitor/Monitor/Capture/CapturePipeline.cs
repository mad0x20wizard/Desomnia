using PacketDotNet;

using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Capture
{
    public class CapturePipeline(IEnumerable<IFragmentReassembly> reassemblers)
    {
        public required ILogger<CapturePipeline> Logger { private get; init; }

        public INetworkCapture? TryCapturePacket(EthernetPacket packet)
        {
            // Tagged frames need VLAN-aware routing and policy scopes, not just reassembly.
            // Reject them here even when custom or disabled BPF admits them.
            if ((ushort)packet.Type is 0x8100 or 0x88a8)
            {
                Logger.LogTrace("Discarding unsupported VLAN-tagged Ethernet frame.");
                return null;
            }

            foreach (var reassembler in reassemblers)
            {
                if (reassembler.TryReassemble(packet, out var capture))
                {
                    return capture; // rejected, reassembled or deferred
                }
            }

            return new EthernetCapture(packet);
        }
    }
}
