namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * What the monitor needs to know about a process – deliberately not a System.Diagnostics.Process.
     *
     * Every member below is something a platform can answer on its own terms, and every one of them
     * an implementation is free to answer lazily: FilePath and ProcessorTime are the expensive two,
     * and the vast majority of the processes on a machine are never asked either.
     */
    public interface IProcess : IDisposable
    {
        int Id { get; }
        int SessionId { get; }

        string Name { get; }

        string? ImagePath { get; }

        /// <summary>Processor time consumed since the process started, or null if it cannot be sampled (any more).</summary>
        TimeSpan? ProcessorTime { get; }

        /// <summary>
        /// Graphics processor time consumed since the process started, or null where nothing can
        /// answer. Summed across every engine of every adapter the platform accounts, so – exactly
        /// like the processor time on a multi-core machine – it can grow faster than the wall
        /// clock. Null means "nobody could measure", never "no GPU work": a process the platform
        /// has no graphics record of has honestly used none, and answers zero.
        /// </summary>
        TimeSpan? GraphicsProcessorTime => null;

        /// <summary>
        /// Whose clock <see cref="GraphicsProcessorTime"/> reads. Where a platform accounts per
        /// process this is the process itself; where it bills a whole group – macOS keeps its
        /// graphics time per coalition, an app together with everything it spawned – every member
        /// answers the same key, and a watch holding several of them counts the shared clock once
        /// instead of once per member.
        /// </summary>
        object GraphicsProcessorScope => this;

        /// <summary>
        /// Bytes read from and written to storage since the process started, or null if it cannot
        /// be sampled (any more). "Storage" is each platform's closest honest answer — see the
        /// implementations for what exactly is counted — but never network traffic.
        /// </summary>
        ProcessInputOutput? StorageData => null;

        /// <summary>
        /// Bytes received and sent over the network, or null where nothing can answer — only
        /// Windows has a per-process source. How precisely it answers is the platform's affair:
        /// an approximation from polled counters, or actual payload bytes where the platform was
        /// configured to meter them.
        /// </summary>
        ProcessInputOutput? NetworkData => null;

        IProcess? Parent { get; }
        bool HasParent(IProcess parent)
        {
            IProcess process = this;

            while (process.Parent != null)
            {
                if (process.Parent == parent)
                    return true;

                process = process.Parent;
            }

            return false;
        }

        bool HasStopped { get; }
        Task Stop(TimeSpan timeout = default);
        event EventHandler Stopped;

        System.Diagnostics.Process Native { get; }
    }

    /**
     * A pair of monotonic byte counters, cumulative since the process started — the shape every
     * platform source happens to share, which is what lets the watch delta them exactly like the
     * processor time.
     *
     * The same struct serves both metrics: for storage IO, In is bytes read and Out is bytes
     * written; for network traffic, In is bytes received and Out is bytes sent. Monotonic is a
     * promise of intent, not of arithmetic — the macOS write ledger can be debited — so consumers
     * clamp their deltas rather than trust the direction.
     */
    // Deliberately no Total property: the one consumer clamps its deltas per field, and a ready
    // -made sum is exactly the shortcut that would quietly clamp per process instead.
    public readonly record struct ProcessInputOutput(long BytesIn, long BytesOut);

}
