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
                // will not start a refresh underneath us); roster entries may carry a decoration
                // around the platform's process, so the concrete layer is unwrapped here
                foreach (var process in this)
                {
                    WatchForExit(process.Layer<LinuxProcess>());
                }
            }
        }

        // internal for the creation middleware, which reports what it just activated – always the
        // concrete process, because the middleware runs before any decoration wraps it
        internal void WatchForExit(LinuxProcess process)
        {
            if (_watcher is EpollProcessExitWatcher watcher)
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
    }
}
