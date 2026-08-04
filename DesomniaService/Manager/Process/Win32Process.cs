using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace MadWizard.Desomnia.Processes.Manager
{
    internal class Win32Process(ProcessInformation info, IProcess? parent) : ProcessHandle(info, parent)
    {
        public required ILogger Logger { private get; init; }

        private readonly Lock _gate = new();

        private RegisteredWaitHandle? _registration;
        private WaitHandle? _signal;

        #region Win32 Metrics
        /**
         * The processor time, asked of the kernel rather than of a process object.
         *
         * This is the one member the monitor samples on a schedule – once a cycle for every process a
         * configured minCPU threshold applies to – and it was the last routine reason to materialize
         * a System.Diagnostics.Process, which builds a description of every process on the machine
         * before it will hand back one number. A handle answers in a syscall, and answers nothing
         * when the process has gone, which is exactly what a sample of it is worth by then.
         */
        public override TimeSpan? ProcessorTime => Win32ProcessManager.QueryProcessorTime(Id);

        /// <summary>Sampled like the processor time: a limited handle, one syscall, null once the process is gone.</summary>
        public override ProcessInputOutput? StorageData => Win32ProcessManager.QueryIO(Id);

        /**
         * The passive approximation, and nothing else: precise metering is not this class'
         * business any more – when it is configured, a decorator wraps this process and answers
         * from the account the meter books into, falling back to exactly this member. That keeps
         * every future metric a wrapper away instead of another region in here.
         */
        public override ProcessInputOutput? NetworkData => Win32ProcessManager.QueryTraffic(Id);
        #endregion

        /**
         * Whether the process is still there – the last of the routine questions the BCL object was
         * kept around for. The indexer asks it of every process it hands out, and the session bridge
         * asks it of its minion before and after every attempt to stop it.
         *
         * The watch already holds the one handle that answers this exactly: a process handle is
         * signalled when its process ends, so a zero-length wait on it is the whole question in a
         * single syscall – against three for opening the pid anew, and about the *right* process,
         * where a re-opened pid may since have been handed to a stranger. Once the exit has been
         * reported the answer is a settled fact and costs nothing. Only a process that never gave
         * the watch a handle is asked the long way – and where the kernel refuses even that, the
         * BCL gets a turn, which is where it would have been asked anyway.
         */
        public override bool HasStopped
        {
            get
            {
                lock (_gate)
                {
                    if (_signal is not null)
                        return _signal.WaitOne(0);

                    if (_stopped)
                        return true;
                }

                return Win32ProcessManager.QueryHasStopped(Id) ?? base.HasStopped;
            }
        }

        /**
         * Waits on the process handle, which Windows signals the moment the process ends – the same
         * mechanism the BCL's Exited event is built on, minus the process object to hang it off.
         *
         * This is the whole of it on Windows: nothing subscribes to a BCL Exited event any more, and
         * the trace session – which reports every exit on the machine while it runs – runs only while
         * something is subscribed to the manager's own events. That can end long before the process
         * does, and whoever holds the process would then wait for an event nothing was left to raise.
         * So it costs a kernel handle and a thread-pool wait per tracked process, deliberately.
         */
        internal void WatchForExit()
        {
            var handle = new SafeWaitHandle(Win32ProcessManager.OpenProcess(Win32ProcessManager.SYNCHRONIZE, false, Id), ownsHandle: true);

            if (handle.IsInvalid)
            {
                handle.Dispose(); // already gone, or not ours to wait on

                return;
            }

            lock (_gate)
            {
                _signal = new Win32ProcessSignal(handle);

                _registration = ThreadPool.RegisterWaitForSingleObject(_signal, (state, timedOut) => TriggerStop(), null, Timeout.Infinite, executeOnlyOnce: true);
            }
        }

        /**
         * Gives up the wait, without a word to anybody.
         *
         * For a process that has ended this is half of reporting it; for one this manager is simply
         * letting go of – a duplicate that lost the race to be tracked, or everything at all when the
         * configuration is rebuilt underneath us – it is the whole of it. The handle would otherwise
         * be held until the process itself ends, which is exactly the lifetime nobody is watching.
         */
        internal void StopWatching()
        {
            lock (_gate)
            {
                _registration?.Unregister(null);
                _registration = null;

                _signal?.Dispose(); // with it the process handle, which is what kept the pid ours
                _signal = null;
            }
        }

        protected override void TriggerStop()
        {
            StopWatching();

            try
            {
                base.TriggerStop();
            }
            catch (Exception ex)
            {
                // the wait fires on a thread-pool thread, where an exception is not something anybody
                // is left to catch – it would take the service down with whoever was listening
                Logger.LogError(ex, "Reporting the exit of '{name}' ({pid}) failed", Name, Id);
            }
        }

        public override void Dispose()
        {
            StopWatching();

            base.Dispose();
        }
    }

    /**
     * Something the thread pool can wait on, over a handle we already hold.
     *
     * Deliberately not a ManualResetEvent: its constructor creates a kernel event, and assigning
     * SafeWaitHandle then replaces the field without closing the one it just made – so every wait
     * would cost two handles instead of one and leave the orphan to a finalizer to notice.
     * WaitHandle's own constructor allocates nothing.
     */
    file sealed class Win32ProcessSignal : WaitHandle
    {
        public Win32ProcessSignal(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
}
