using MadWizard.Desomnia.Network.Capture;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Datagram
{
    /// <summary>
    /// A received UDP datagram, decoupled from its inlet: sniffed off the wire (with the Ethernet
    /// frame at hand), or delivered through an OS socket (kernel-reassembled, no frame). Responses
    /// travel back out the same inlet -- see <see cref="TryRespond"/>.
    /// </summary>
    public class DatagramPacket
    {
        public PhysicalAddress? SourcePhysicalAddress { get; }

        public IPEndPoint Source { get; }
        public IPEndPoint Target { get; }

        public byte[] Payload { get; }

        private readonly Action<byte[]>? _respond;

        internal DatagramPacket(INetworkCapture capture)
        {
            SourcePhysicalAddress = capture.SourcePhysicalAddress;

            Source = capture.SourceEndPoint ?? throw new ArgumentException("Source endpoint missing");
            Target = capture.TargetEndPoint ?? throw new ArgumentException("Target endpoint missing");

            Payload = capture.PayloadData;
        }

        internal DatagramPacket(Action<byte[]> respond, IPEndPoint source, IPEndPoint target, byte[] payload)
        {
            _respond = respond;

            Source = source;
            Target = target;

            Payload = payload;
        }

        /// <summary>
        /// Sends <paramref name="payload"/> back to the datagram's source, when the inlet supports
        /// direct replies (the OS socket). A sniffed datagram cannot be answered this way -- the
        /// response must be crafted onto the wire instead.
        /// </summary>
        public bool TryRespond(byte[] payload)
        {
            if (_respond is null)
                return false;

            _respond(payload);

            return true;
        }
    }
}
