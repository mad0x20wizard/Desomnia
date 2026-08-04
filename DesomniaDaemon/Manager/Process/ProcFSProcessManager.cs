using MadWizard.Desomnia.Processes.Manager.Native;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>
    /// Linux does have a real event source for process lifetime — the netlink proc connector — but
    /// reaching it needs raw socket P/Invoke that .NET cannot express, so this polls for now. What
    /// it does not do is poll through the BCL, whose Linux enumeration opens and parses
    /// /proc/[pid]/stat, /proc/[pid]/status and /proc/[pid]/cmdline for every process, and then a
    /// stat file for every thread of every process, to answer a question about ids.
    ///
    /// Here a poll is one directory read: /proc *is* the process list. Only ids the diff finds
    /// genuinely new are described, and the one stat line that describes them carries the name, the
    /// parent and the session together — so <c>watchChildren</c> works on this platform at all.
    /// </summary>
    internal sealed class ProcFSProcessManager : PollingProcessManager
    {
        private EpollProcessExitWatcher? _watcher;

        public ProcFSProcessManager(TimeSpan interval) : base(interval)
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
                    _watcher = new EpollProcessExitWatcher(Logger);
                }
                catch (Exception ex)
                {
                    // no worse than before it existed: exits are then noticed by the next poll
                    Logger.LogWarning(ex, "Falling back to polling for process exits");

                    return;
                }

                // catch up on everything already tracked (the enumeration guard sees this lock and
                // will not start a refresh underneath us)
                foreach (var process in this)
                {
                    WatchForExit(process);
                }
            }
        }

        private void WatchForExit(IProcess process)
        {
            if (_watcher is EpollProcessExitWatcher watcher && process is LinuxProcess linux)
            {
                watcher.Watch(linux.Id, linux.TriggerStop);
            }
        }

        public override void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;

            base.Dispose();
        }

        protected override IEnumerable<ProcessInformation> EnumerateProcesses()
        {
            return ProcFs.EnumeratePIDs().Select(pid => new ProcessInformation(pid));
        }

        protected override ProcessInformation? QueryProcess(int pid)
        {
            if (ProcFs.ReadStat(pid) is not ProcFs.Stat stat)
                return null; // gone again between the listing and the read

            if (stat.State == ProcFs.Zombie)
                return null; // exited, and only still here because nobody has reaped it yet

            return new ProcessInformation(pid)
            {
                Name = ProcFs.ResolveName(stat.Command, ProcFs.ReadExecutablePath(pid)),
                SessionId = stat.SessionId,
                // pid 0 is not a process, and the kernel reparents orphans rather than leaving loops
                ParentId = stat.ParentId > 0 && stat.ParentId != pid ? stat.ParentId : null,
            };
        }

        // Created here rather than through the container: a process is a value the manager already
        // holds every ingredient for, and resolving one per pid would put the container on the path
        // of a startup that materialises several hundred of them.
        protected override IProcess CreateProcess(ProcessInformation entry, IProcess? parent)
        {
            var process = new LinuxProcess(entry, parent);

            WatchForExit(process);

            return process;
        }

        /// <summary>
        /// Answers from procfs everything the monitor asks per cycle — the executable path a
        /// path-shaped pattern is matched against, whether the process is still there, and the
        /// storage IO a configured <c>minIO</c> samples. Only sampling processor time (under a
        /// configured <c>minCPU</c>) and stopping a process on demand still reach for the BCL
        /// object the base creates lazily, and neither happens unless the configuration asked
        /// for it.
        /// </summary>
        private sealed class LinuxProcess(ProcessInformation entry, IProcess? parent) : ProcessHandle(entry, parent)
        {
            public override string? ImagePath => ProcFs.ReadExecutablePath(Id);

            // Storage bytes from /proc/[pid]/io (see ProcFs.ReadIO for what counts). One kernel
            // quirk is accepted rather than corrected: a parent that reaps a child inherits the
            // child's lifetime totals in a single jump — IO has no cutime/cstime analog — so the
            // cycle after a watched child exits can report demand once too often. For a keep-awake
            // signal that errs in the benign direction.
            public override ProcessInputOutput? StorageData => ProcFs.ReadIO(Id) is ProcFs.IO io ? new ProcessInputOutput(io.ReadBytes, io.WriteBytes) : null;

            // procfs keeps a directory for a process that has exited until its parent reaps it, so
            // existence alone is not life — the state is what says so
            public override bool HasStopped => ProcFs.ReadStat(Id) is not ProcFs.Stat stat || stat.State == ProcFs.Zombie;

            // SIGTERM is the only "please stop" this platform offers a daemon; a process that has
            // installed a handler unwinds, one that has not dies where SIGKILL would have killed it
            // anyway – so it costs nothing to ask.
            protected override bool RequestStop() => Signals.TrySend(Id, Signals.SIGTERM, out _);

            /// <summary>The kernel says it has ended; the manager is listening for exactly this.</summary>
            internal new void TriggerStop() => base.TriggerStop();
        }
    }
}
