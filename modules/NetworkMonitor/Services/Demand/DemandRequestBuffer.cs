using PacketDotNet;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Network.Demand
{
    public partial class DemandRequestBuffer : IIEnumerable<EthernetPacket>
    {
        private Channel<EthernetPacket> IncomingQueue { get => field ??= Channel.CreateUnbounded<EthernetPacket>(); } = null!;
        private Queue<EthernetPacket> OutgoingQueue { get => field ??= new Queue<EthernetPacket>(); } = null!;

        public async IAsyncEnumerable<EthernetPacket> ReadPackets(TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);

            while (await DequeuePacket(cts.Token) is EthernetPacket packet)
            {
                OutgoingQueue.Enqueue(packet);

                yield return packet;
            }
        }

        public void EnqueuePacket(EthernetPacket packet)
        {
            IncomingQueue.Writer.TryWrite(packet);
        }

        private async Task<EthernetPacket?> DequeuePacket(CancellationToken token)
        {
            try
            {
                var packet = await IncomingQueue.Reader.ReadAsync(token);

                return packet;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public IEnumerator<EthernetPacket> GetEnumerator()
        {
            return OutgoingQueue.GetEnumerator();
        }
    }
}
