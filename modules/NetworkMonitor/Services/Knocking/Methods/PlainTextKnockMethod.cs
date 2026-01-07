using MadWizard.Desomnia.Network.Knocking.Secrets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Knocking.Methods
{
    public class PlainTextKnockMethod : IKnockMethod, IKnockDetector
    {
        public required ILogger<PlainTextKnockMethod> Logger { private get; init; }

        void IKnockMethod.Knock(IPAddress source, IPEndPoint endpoint, IPPort knock, SharedSecret secret)
        {
            using UdpClient udp = new(endpoint.AddressFamily);

            Logger.LogTrace($"Knocking at {endpoint.Address} using {knock.Port}/udp");

            var bytes = udp.Send(secret.Key, new IPEndPoint(endpoint.Address, knock.Port));
        }

        IEnumerable<KnockEvent> IKnockDetector.Examine(IPPacket packet, SharedSecret secret)
        {
            if (packet.PayloadPacket is UdpPacket udp)
            {
                if (udp.PayloadData.SequenceEqual(secret.Key))
                {
                    //Logger.LogInformation($"Received valid plain-text knock from {packet.SourceAddress} to {packet.DestinationAddress}:{udp.DestinationPort}/udp");

                    yield return new(packet.SourceAddress);
                }
            }
        }

    }
}
