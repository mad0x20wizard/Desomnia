using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace MadWizard.Desomnia.Processes.Manager
{
    internal class Win32Process(ProcessInformation info, IProcess? parent, Win32ProcessManager manager) : ProcessHandle(info, parent)
    {
        private readonly Lock _gate = new();

        private RegisteredWaitHandle? _registration;
        private WaitHandle? _signal;

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

        #region Traffic account
        private long _bytesReceived, _bytesSent;
        private volatile bool _metered;

        /**
         * Answered from this process' own account while a meter books into it, and from the
         * passive counters otherwise. The account never asks who is booking – the meter finds
         * the process, not the other way around – which is also why the fallback is per process:
         * a process the meter has not seen transfer anything has an empty account, and an empty
         * account is indistinguishable from an unmetered one.
         */
        public override ProcessInputOutput? NetworkData
        {
            get
            {
                if (_metered)
                {
                    return new ProcessInputOutput(Interlocked.Read(ref _bytesReceived), Interlocked.Read(ref _bytesSent));
                }

                return Win32ProcessManager.QueryTraffic(Id);
            }
        }

        /// <summary>Books metered bytes into the account – and switches the answer over to it, from the first byte on.</summary>
        internal void BookTraffic(long received = 0, long sent = 0)
        {
            if (received > 0)
                Interlocked.Add(ref _bytesReceived, received);
            if (sent > 0)
                Interlocked.Add(ref _bytesSent, sent);

            _metered = true;
        }

        /**
         * The meter is gone; the account must not go on being the answer, because numbers nobody
         * maintains would read as a process that stopped transferring. The totals themselves
         * stay – a later meter books on top of them, keeping the account monotonic.
         */
        internal void ClearTraffic() => _metered = false;
        #endregion

        /**
         * Whether the process is still there – the last of the routine questions the BCL object was
         * kept around for. The indexer asks it of every process it hands out, and the session bridge
         * asks it of its minion before and after every attempt to stop it.
         *
         * Only where the kernel refuses to say does the BCL get a turn, which is where it would have
         * been asked anyway: a process we cannot open is one we can only ask about second-hand.
         */
        public override bool HasStopped => Win32ProcessManager.QueryHasStopped(Id) ?? base.HasStopped;

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
                manager.Log.LogError(ex, "Reporting the exit of '{name}' ({pid}) failed", Name, Id);
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
