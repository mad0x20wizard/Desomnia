using MadWizard.Desomnia.Network.Capture;
using PacketDotNet;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Network.Demand
{
    public partial class DemandRequestBuffer : IIEnumerable<INetworkCapture>
    {
        private Channel<INetworkCapture> IncomingQueue { get => field ??= Channel.CreateUnbounded<INetworkCapture>(); } = null!;
        private Queue<INetworkCapture> OutgoingQueue { get => field ??= new Queue<INetworkCapture>(); } = null!;

        public async IAsyncEnumerable<INetworkCapture> ReadPackets(TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);

            while (await DequeuePacket(cts.Token) is INetworkCapture packet)
            {
                OutgoingQueue.Enqueue(packet);

                yield return packet;
            }
        }

        public void EnqueuePacket(INetworkCapture packet)
        {
            IncomingQueue.Writer.TryWrite(packet);
        }

        private async Task<INetworkCapture?> DequeuePacket(CancellationToken token)
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

        public IEnumerator<INetworkCapture> GetEnumerator()
        {
            return OutgoingQueue.GetEnumerator();
        }
    }
}
