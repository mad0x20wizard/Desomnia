using PacketDotNet;

namespace MadWizard.Desomnia.Network.Capture
{
    public interface IFragmentReassembly
    {
        /// <summary>
        /// Returns false when this reassembler does not handle the frame (capture is null).
        /// Returns true when it consumes the frame: capture is the completed or passthrough
        /// capture, or null when the frame is held or rejected. A true result stops the pipeline.
        /// </summary>
        public bool TryReassemble(EthernetPacket packet, out INetworkCapture? capture);
    }
}
