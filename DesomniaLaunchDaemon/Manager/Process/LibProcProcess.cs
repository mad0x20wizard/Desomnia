using MadWizard.Desomnia.LaunchDaemon.Native;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>
    /// Answers from libproc everything the monitor asks per cycle — the executable path a
    /// path-shaped pattern is matched against, whether the process is still there, and the
    /// storage IO a configured <c>minIO</c> samples. Only sampling processor time (under a
    /// configured <c>minCPU</c>) and stopping a process on demand still reach for the BCL
    /// object the base creates lazily, and neither happens unless the configuration asked
    /// for it.
    /// </summary>
    internal sealed class LibProcProcess(ProcessInformation info, IProcess? parent) : ProcessHandle(info, parent)
    {
        public override string? ImagePath => LibProc.GetProcessPath(Id);

        /**
         * Storage IO as this kernel accounts it, which is stricter than the other platforms:
         * reads are physical block-device transfers — macOS keeps no logical read counter
         * anywhere, so a process rereading a cached file is invisible here (its CPU time is
         * what shows). Writes sum the logical ledger with the physical counter, because each
         * alone lies differently: the ledger tracks internal storage only (a copy onto a USB
         * drive would not move it), the physical counter attributes buffered writes to the
         * kernel's flusher instead of the writer. Direct IO lands in both, an overcount that
         * errs toward demand — the safe direction for a keep-awake signal. The ledger is also
         * debited when dirtied pages are invalidated, which is one reason the watch clamps
         * its deltas at zero rather than trusting the direction.
         */
        public override ProcessInputOutput? StorageData
        {
            get
            {
                if (LibProc.GetProcessRusage(Id) is LibProc.rusage_info_v4 rusage)
                {
                    return new ProcessInputOutput((long)rusage.ri_diskio_bytesread, (long)(rusage.ri_logical_writes + rusage.ri_diskio_byteswritten));
                }

                return base.StorageData;
            }
        }

        public override bool HasStopped => LibProc.GetProcessInfo(Id) == null;

        // SIGTERM is the only "please stop" this platform offers a daemon; a process that has
        // installed a handler unwinds, one that has not dies where SIGKILL would have killed it
        // anyway – so it costs nothing to ask.
        protected override bool RequestStop() => Signals.TrySend(Id, Signals.SIGTERM, out int error);

        /// <summary>The kernel says it has ended; the manager is listening for exactly this.</summary>
        internal new void TriggerStop() => base.TriggerStop();
    }
}
