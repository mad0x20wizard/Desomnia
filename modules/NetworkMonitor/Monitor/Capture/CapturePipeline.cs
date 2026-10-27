using PacketDotNet;

namespace MadWizard.Desomnia.Network.Capture
{
    public class CapturePipeline(IEnumerable<IFragmentReassembly> reassemblers)
    {
        public INetworkCapture? TryCapturePacket(EthernetPacket packet)
        {
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
