using MadWizard.Desomnia.LaunchDaemon.Native;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>
    /// macOS has no unprivileged way to be *told* that a process started — Endpoint Security would
    /// know, but only for software Apple has granted the entitlement to. So this still polls; what
    /// it does not do is ask the BCL, whose macOS enumeration builds a full ProcessInfo for every
    /// process on the machine, walking each of its threads, to answer a question about ids.
    ///
    /// Here a poll is <see cref="LibProc.EnumeratePIDs"/> and nothing else: one syscall, one int
    /// array. Only ids the diff finds genuinely new are described, and describing one costs three
    /// calls — which is also where the parent comes from, the thing the BCL has no cross-platform
    /// way to report, and therefore why <c>watchChildren</c> works on this platform at all.
    /// </summary>
    internal sealed class LibProcProcessManager : PollingProcessManager
    {
        private KQueueProcessExitWatcher? _watcher;

        public LibProcProcessManager(TimeSpan interval) : base(interval)
        {
            // the same bargain the Windows ETW manager strikes: the kernel is only asked to report
            // anything while somebody is actually listening for it
            ListenerCountChanged += (sender, count) => ConfigureWatcher();
        }

        private void ConfigureWatcher()
        {
            lock (this)
            {
                if (ListenerCount == 0)
                {
                    _watcher?.Dispose();
                    _watcher = null;

                    return;
                }

                if (_watcher != null)
                    return;

                try
                {
                    _watcher = new KQueueProcessExitWatcher(Logger);
                }
                catch (Exception ex)
                {
                    // no worse than before it existed: exits are then noticed by the next poll
                    Logger.LogWarning(ex, "Falling back to polling for process exits");

                    return;
                }

                // catch up on everything already tracked (the enumeration guard sees this lock and
                // will not start a refresh underneath us)
                foreach (var process in this.OfType<LibProcProcess>())
                {
                    WatchForExit(process);
                }
            }
        }

        protected override IEnumerable<ProcessInformation> EnumerateProcesses()
        {
            return LibProc.EnumeratePIDs().Select(pid => new ProcessInformation(pid));
        }

        protected override ProcessInformation? QueryProcess(int pid)
        {
            if (LibProc.GetProcessInfo(pid) is not LibProc.proc_bsdshortinfo info)
                return null; // a zombie the list still carries, pid 0, or simply gone again

            return new ProcessInformation(pid)
            {
                // The executable name, which is what the BCL reports on this platform too, and the
                // only form long enough for a pattern like "com.apple.WebKit.WebContent" to match.
                // The kernel's command name is the fallback, and it stops after 15 characters.
                Name = Path.GetFileName(LibProc.GetProcessPath(pid)) is { Length: > 0 } name ? name : info.GetCommand(),
                SessionId = LibProc.GetSessionId(pid),
                // launchd's parent is the kernel, which is not a process anybody can watch
                ParentId = info.pbsi_ppid > 0 && info.pbsi_ppid != (uint)pid ? (int)info.pbsi_ppid : null,
            };
        }

        // Created here rather than through the container: a process is a value the manager already
        // holds every ingredient for, and resolving one per pid would put the container on the path
        // of a startup that materialises several hundred of them.
        protected override IProcess CreateProcess(ProcessInformation entry, IProcess? parent)
        {
            var process = new LibProcProcess(entry, parent);

            WatchForExit(process);

            return process;
        }

        private void WatchForExit(LibProcProcess process)
        {
            if (_watcher is KQueueProcessExitWatcher watcher)
            {
                watcher.Watch(process.Id, process.TriggerStop);
            }
        }

        public override void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;

            base.Dispose();
        }

        /// <summary>
        /// Answers from libproc everything the monitor asks per cycle — the executable path a
        /// path-shaped pattern is matched against, whether the process is still there, and the
        /// storage IO a configured <c>minIO</c> samples. Only sampling processor time (under a
        /// configured <c>minCPU</c>) and stopping a process on demand still reach for the BCL
        /// object the base creates lazily, and neither happens unless the configuration asked
        /// for it.
        /// </summary>
        private sealed class LibProcProcess(ProcessInformation info, IProcess? parent) : ProcessHandle(info, parent)
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
}
