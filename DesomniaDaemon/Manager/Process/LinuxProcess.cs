using MadWizard.Desomnia.Processes.Manager.Native;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>
    /// Answers from procfs everything the monitor asks per cycle — the executable path a
    /// path-shaped pattern is matched against, whether the process is still there, and the
    /// storage IO a configured <c>minIO</c> samples. Only sampling processor time (under a
    /// configured <c>minCPU</c>) and stopping a process on demand still reach for the BCL
    /// object the base creates lazily, and neither happens unless the configuration asked
    /// for it.
    /// </summary>
    internal sealed class LinuxProcess(ProcessInformation entry, IProcess? parent) : ProcessHandle(entry, parent)
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
