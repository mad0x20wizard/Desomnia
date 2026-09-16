using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    /**
     * The kernel's graphics accounting, which macOS keeps per *coalition* rather than per process.
     *
     * A resource coalition is an application together with everything it spawned – its XPC helpers
     * and forked children – and on Apple Silicon it is the only place GPU time is booked at all:
     * the graphics driver reports through a kernel routine that credits the coalition of whatever
     * task submitted the work, while the per-task counter the Intel Macs used (TASK_POWER_INFO_V2)
     * is compiled out on arm64 and answers a permanent zero. Measured on an M-series machine, a
     * Moonlight session bills its decode helper's work to the app's own coalition, which is the
     * grouping this monitor wants anyway: a per-process reading would have missed it entirely,
     * because the decoding happens in a launchd-spawned service nobody would think to watch.
     *
     * Neither call needs a task port, an entitlement, or root – the coalition query carries no
     * privilege check at all, and the pid-to-coalition flavor is on the kernel's explicit list of
     * questions any user may ask about any process.
     */
    public static unsafe partial class Coalitions
    {
        /// <summary>The resource coalition (as opposed to the jetsam one), which is where the ledger lives.</summary>
        private const int COALITION_TYPE_RESOURCE = 0;

        /// <summary>Not in the public SDK headers, but a stable part of the kernel ABI.</summary>
        private const int PROC_PIDCOALITIONINFO = 20;

        /**
         * The daemon's own coalition, asked once at startup.
         *
         * Everything the daemon launches is billed here alongside the daemon itself, so this
         * ledger says nothing about any single one of them – which makes it the one coalition
         * whose graphics time must never be reported as a watched process' own.
         */
        public static ulong? Own { get; } = ResourceCoalitionOf(Environment.ProcessId);

        /// <summary>The coalition a process is billed to – fixed for its lifetime – or null once it is gone.</summary>
        public static ulong? ResourceCoalitionOf(int pid)
        {
            ProcessCoalitionInfo info = default;

            if (proc_pidinfo(pid, PROC_PIDCOALITIONINFO, 0, &info, sizeof(ProcessCoalitionInfo)) != sizeof(ProcessCoalitionInfo))
                return null;

            return info.Coalitions[COALITION_TYPE_RESOURCE];
        }

        /**
         * The graphics time booked to the coalition since it came into being, or null for one the
         * kernel no longer knows – an app that has quit takes its coalition with it, and the
         * relaunched one starts a new ledger at zero under a new id.
         *
         * The kernel counts nanoseconds here (unlike the processor time in the same structure,
         * which is in mach units), so the conversion is a plain division into ticks.
         */
        public static TimeSpan? GraphicsTimeOf(ulong coalition)
        {
            // zeroed on purpose: the kernel fills what its own structure has and reports neither
            // how much that was nor leaves the rest alone, so a shorter one must find zeroes
            CoalitionResourceUsage usage = default;

            if (coalition_info_resource_usage(coalition, &usage, (nuint)sizeof(CoalitionResourceUsage)) != 0)
                return null;

            return TimeSpan.FromTicks((long)(usage.GraphicsTime / 100));
        }

        /// <summary>proc_pidcoalitioninfo: one coalition id per type, and a reserved tail.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessCoalitionInfo
        {
            internal unsafe fixed ulong Coalitions[2];
            internal unsafe fixed ulong Reserved[3];
        }

        /**
         * struct coalition_resource_usage, named as far as the field this asks for and sized well
         * beyond it: the structure has only ever grown (the newest release appends GPU and neural
         * -engine energy counters past everything below), and asking for more than the running
         * kernel has costs nothing, while asking for less than a future one would still work.
         */
        [StructLayout(LayoutKind.Sequential, Size = 512)]
        private struct CoalitionResourceUsage
        {
            internal ulong TasksStarted;
            internal ulong TasksExited;
            internal ulong TimeNonEmpty;
            internal ulong ProcessorTime;   // mach units, unlike the graphics time below
            internal ulong InterruptWakeups;
            internal ulong PlatformIdleWakeups;
            internal ulong BytesRead;
            internal ulong BytesWritten;
            internal ulong GraphicsTime;    // nanoseconds
        }

        [LibraryImport("/usr/lib/libSystem.B.dylib")]
        private static partial int proc_pidinfo(int pid, int flavor, ulong argument, void* buffer, int bufferSize);

        [LibraryImport("/usr/lib/libSystem.B.dylib")]
        private static partial int coalition_info_resource_usage(ulong coalition, CoalitionResourceUsage* usage, nuint bufferSize);
    }
}
