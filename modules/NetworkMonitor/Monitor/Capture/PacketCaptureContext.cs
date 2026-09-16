using SharpPcap;
using System.Collections.Concurrent;

namespace MadWizard.Desomnia.Network
{
    internal class PacketCaptureContext : IIEnumerable<RawCapture>, IDisposable
    {
        private string Name { get; }

        private SemaphoreSlim PacketQueueSlots { get; }

        private BlockingCollection<RawCapture> PacketQueue { get; }

        private Thread? ProcessingThread { get; set; }

        internal PacketCaptureContext(string name, int capacity)
        {
            Name = name;

            PacketQueueSlots = new SemaphoreSlim(capacity, capacity);

            PacketQueue = new BlockingCollection<RawCapture>(new ConcurrentQueue<RawCapture>(), capacity);
        }

        internal void StartProcessing(Action<PacketCaptureContext> processor)
        {
            ProcessingThread = new Thread(() => processor(this))
            {
                Name = $"PacketProcessor:{Name}",

                IsBackground = true
            };

            ProcessingThread.Start();
        }

        internal bool EnqueueCapture(PacketCapture capture)
        {
            if (PacketQueueSlots.Wait(0))
            {
                using var slot = new QueueSlot(this);

                RawCapture raw = capture.GetPacket();

                slot.EnqueueCapture(raw);

                return true;
            }

            return false;
        }

        internal void StopProcessing(TimeSpan timeout = default)
        {
            if (!PacketQueue.IsCompleted)
            {
                PacketQueue.CompleteAdding();
            }

            if (!(ProcessingThread?.Join(timeout == default ? Timeout.InfiniteTimeSpan : timeout) ?? true))
            {
                throw new TimeoutException($"Thread '{ProcessingThread.Name}' did not finish after {timeout}.");
            }

            ProcessingThread = null;
        }

        public IEnumerator<RawCapture> GetEnumerator()
        {
            IEnumerator<RawCapture> enumerator;

            try
            {
                enumerator = PacketQueue.GetConsumingEnumerable().GetEnumerator();
            }
            catch (ObjectDisposedException)
            {
                throw new CapturingStoppedException();
            }

            while (true)
            {
                RawCapture? raw = null;

                try
                {
                    if (enumerator.MoveNext())
                    {
                        raw = enumerator.Current;
                    }
                }
                catch (InvalidOperationException)
                {
                    throw new CapturingStoppedException();
                }

                if (raw is not null)
                {
                    // The queued frame no longer consumes backlog capacity while it is processed.
                    PacketQueueSlots.Release();

                    yield return raw;
                }
                else
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            PacketQueueSlots.Dispose();

            PacketQueue.Dispose();
        }

        private ref struct QueueSlot(PacketCaptureContext ctx) : IDisposable
        {
            bool _releaseOnDisposal = true;

            internal bool EnqueueCapture(RawCapture capture)
            {
                try
                {
                    if (ctx.PacketQueue.TryAdd(capture))
                    {
                        _releaseOnDisposal = false;

                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    throw CapturingStoppedException.CachedInstance;
                }

                return false;
            }

            void IDisposable.Dispose()
            {
                if (_releaseOnDisposal)
                {
                    ctx.PacketQueueSlots.Release();
                }
            }
        }
    }
}
