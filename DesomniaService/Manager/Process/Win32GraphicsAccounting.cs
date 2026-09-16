using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * The graphics clock, asked of the display kernel the way Task Manager asks it: the scheduler
     * keeps a running time per process, per adapter, per engine node, in the same 100-nanosecond
     * units a TimeSpan counts in. The sum over every node of every hardware adapter is the
     * process' graphics processor time – engines add up like cores, so it can outgrow the wall
     * clock, which is exactly how the watch reads the CPU on a multi-core machine.
     *
     * D3DKMT_QUERYSTATISTICS is officially "reserved for system use", but the public SDK ships
     * the full struct behind a compile-time size lock (C_ASSERT == 0x328), the query enum has
     * only ever been appended to, and Process Hacker and LibreHardwareMonitor have leaned on this
     * exact query for over a decade. The offsets below are that locked x64 layout, verified live
     * against the "\GPU Engine" performance counters, which surface the same scheduler data.
     */
    internal static partial class Win32GraphicsAccounting
    {
        /**
         * The machine's hardware adapters: what the last enumeration found and when. Believed for
         * a minute, then asked anew – adapters come and go under a running service (an eGPU dock,
         * a driver update recreating the device under a new LUID), and a set kept forever would
         * turn a hot-plugged adapter's real work into a permanent, honest-looking zero. A cached
         * adapter that stops answering outright forces the refresh early.
         */
        private static volatile AdapterCache? _cache;

        private sealed record AdapterCache(Adapter[]? Adapters, long Timestamp);

        private static readonly long RefreshMilliseconds = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;

        private readonly record struct Adapter(uint LuidLow, uint LuidHigh, uint NodeCount);

        /**
         * The graphics time the process has consumed, zero for one the scheduler has no record
         * of, or null where nothing could be asked – no adapter, no handle – which the watch
         * reports as an unreadable configured metric.
         */
        internal static TimeSpan? QueryTime(int pid)
        {
            // the statistics refuse the limited handle every other query here gets by on –
            // anything less than PROCESS_QUERY_INFORMATION is STATUS_INVALID_PARAMETER
            using var process = new SafeProcessHandle(Win32ProcessManager.OpenProcess(PROCESS_QUERY_INFORMATION, false, pid), ownsHandle: true);

            if (process.IsInvalid)
                return null;

            for (var rebuilt = false; ; rebuilt = true)
            {
                if (Adapters() is not { Length: > 0 } adapters)
                    return null; // no display kernel to ask, so the platform cannot measure this

                if (SumRunningTime(process, adapters) is long ticks)
                    return TimeSpan.FromTicks(ticks);

                /**
                 * Every node refused. For a process that never touched the GPU that is the normal
                 * answer – no scheduler record is honestly zero time – but it is also what stale
                 * LUIDs produce once a driver restart has re-created every adapter. Only the
                 * adapters can tell the two apart: a live one answers its own statistics no
                 * matter who asks about which process. (A dead LUID hiding behind another
                 * adapter's live answer escapes this check and waits for the minute refresh.)
                 */
                if (rebuilt || Alive(adapters))
                    return TimeSpan.Zero;

                _cache = null; // gone stale – rebuild once, then believe the result
            }
        }

        /// <summary>The running time summed over every node, or null when not one node answered.</summary>
        private static long? SumRunningTime(SafeProcessHandle process, Adapter[] adapters)
        {
            long ticks = 0;
            var answered = false;

            foreach (var adapter in adapters)
            {
                for (uint node = 0; node < adapter.NodeCount; node++)
                {
                    var query = new D3DKMT_QUERYSTATISTICS
                    {
                        Type = D3DKMT_QUERYSTATISTICS_PROCESS_NODE,
                        LuidLow = adapter.LuidLow,
                        LuidHigh = adapter.LuidHigh,
                        ProcessHandle = process.DangerousGetHandle(),
                        NodeId = node,
                    };

                    if (D3DKMTQueryStatistics(ref query) == STATUS_SUCCESS)
                    {
                        answered = true;

                        ticks += query.RunningTime;
                    }
                    // a refusal is a process this node never scheduled – nothing to add
                }
            }

            return answered ? ticks : null;
        }

        /// <summary>Whether every cached adapter still answers for itself.</summary>
        private static bool Alive(Adapter[] adapters)
        {
            foreach (var adapter in adapters)
            {
                var query = new D3DKMT_QUERYSTATISTICS
                {
                    Type = D3DKMT_QUERYSTATISTICS_ADAPTER,
                    LuidLow = adapter.LuidLow,
                    LuidHigh = adapter.LuidHigh,
                };

                if (D3DKMTQueryStatistics(ref query) != STATUS_SUCCESS)
                    return false;
            }

            return true;
        }

        private static Adapter[]? Adapters()
        {
            if (_cache is { } cache && Environment.TickCount64 - cache.Timestamp < RefreshMilliseconds)
                return cache.Adapters;

            lock (_gate)
            {
                // whoever lost the race to the lock uses the winner's fresh enumeration
                if (_cache is { } current && Environment.TickCount64 - current.Timestamp < RefreshMilliseconds)
                    return current.Adapters;

                var adapters = EnumerateAdapters();

                // a failed enumeration is cached too – a machine without a display kernel would
                // otherwise pay for the question again on every sample of every watched process
                _cache = new AdapterCache(adapters, Environment.TickCount64);

                return adapters;
            }
        }

        private static unsafe Adapter[]? EnumerateAdapters()
        {
            var enumeration = new D3DKMT_ENUMADAPTERS2();

            // the first call is a count probe and answers generously – allocate by its number,
            // iterate by what the filling call corrects it down to
            if (D3DKMTEnumAdapters2(ref enumeration) != STATUS_SUCCESS || enumeration.NumAdapters == 0)
                return null;

            var infos = new D3DKMT_ADAPTERINFO[enumeration.NumAdapters];

            fixed (D3DKMT_ADAPTERINFO* pointer = infos)
            {
                enumeration.Adapters = (nint)pointer;

                if (D3DKMTEnumAdapters2(ref enumeration) != STATUS_SUCCESS)
                    return null;
            }

            var adapters = new List<Adapter>((int)enumeration.NumAdapters);

            for (var i = 0; i < enumeration.NumAdapters; i++)
            {
                var info = infos[i];

                try
                {
                    // only the devices that actually render – see RendersInHardware for what the
                    // others would do to the sum
                    if (!RendersInHardware(info.Adapter))
                        continue;

                    var query = new D3DKMT_QUERYSTATISTICS
                    {
                        Type = D3DKMT_QUERYSTATISTICS_ADAPTER,
                        LuidLow = info.LuidLow,
                        LuidHigh = info.LuidHigh,
                    };

                    if (D3DKMTQueryStatistics(ref query) == STATUS_SUCCESS && query.NodeCount > 0)
                    {
                        adapters.Add(new Adapter(info.LuidLow, info.LuidHigh, query.NodeCount));
                    }
                }
                finally
                {
                    // the enumeration opened a handle per adapter, whether we keep it or not
                    var close = new D3DKMT_CLOSEADAPTER { Adapter = info.Adapter };

                    D3DKMTCloseAdapter(ref close);
                }
            }

            return [.. adapters];
        }

        /**
         * Whether this adapter is a graphics processor in its own right, rather than one of the
         * several ways Windows presents somebody else's.
         *
         * The machine lists more adapters than it has GPUs. A virtual monitor – a remote-desktop
         * display, a headless dongle, one of the indirect-display drivers a KVM or a tablet-as-a
         * -screen tool installs – appears as an adapter of its own, but renders on a real GPU and
         * reports *that* GPU's scheduler statistics as if they were its own. Summed with the card
         * they came from, one process' work is counted once per mirror: measured here on a
         * machine with two such drivers, every process read three times its actual graphics time.
         * They give themselves away by claiming to display without claiming to render.
         *
         * Software rendering is excluded for a different reason: WARP and the Basic Render Driver
         * do their work on the processor, and counted here a CPU fallback would read as graphics
         * demand.
         *
         * Asked leniently: an adapter that will not answer at all is kept. Counting a mirror
         * twice overstates demand, which at worst keeps a machine awake; dropping the one real
         * GPU understates it, which puts a machine to sleep in the middle of the work.
         */
        private static unsafe bool RendersInHardware(uint adapter)
        {
            uint type = 0;

            var query = new D3DKMT_QUERYADAPTERINFO
            {
                Adapter = adapter,
                Type = KMTQAITYPE_ADAPTERTYPE,
                PrivateDriverData = (nint)(&type),
                PrivateDriverDataSize = sizeof(uint),
            };

            if (D3DKMTQueryAdapterInfo(ref query) != STATUS_SUCCESS)
                return true;

            return (type & RENDER_SUPPORTED) != 0 && (type & SOFTWARE_DEVICE) == 0;
        }

        #region Windows-API
        const int STATUS_SUCCESS = 0;

        /// <summary>What the statistics demand of a process handle – the limited flavor is refused outright.</summary>
        const uint PROCESS_QUERY_INFORMATION = 0x0400;

        const uint D3DKMT_QUERYSTATISTICS_ADAPTER = 0;
        const uint D3DKMT_QUERYSTATISTICS_PROCESS_NODE = 6;

        const uint KMTQAITYPE_ADAPTERTYPE = 15;

        /// <summary>The RenderSupported bit of D3DKMT_ADAPTERTYPE – what a mirror of another GPU lacks.</summary>
        const uint RENDER_SUPPORTED = 0x1;

        /// <summary>The SoftwareDevice bit of D3DKMT_ADAPTERTYPE.</summary>
        const uint SOFTWARE_DEVICE = 0x4;

        private static readonly Lock _gate = new();

        /**
         * The 0x328-byte statistics struct, reduced to the members these two queries touch: the
         * result union starts at 24, the query input union at 800. The SDK's C_ASSERT on the
         * total size is what makes hand-picked offsets safe to rely on.
         */
        [StructLayout(LayoutKind.Explicit, Size = 0x328)]
        private struct D3DKMT_QUERYSTATISTICS
        {
            [FieldOffset(0)] internal uint Type;
            [FieldOffset(4)] internal uint LuidLow;
            [FieldOffset(8)] internal uint LuidHigh;
            [FieldOffset(16)] internal nint ProcessHandle;

            [FieldOffset(24)] internal uint NbSegments;   // ADAPTER result
            [FieldOffset(28)] internal uint NodeCount;    // ADAPTER result
            [FieldOffset(24)] internal long RunningTime;  // PROCESS_NODE result, in 100ns ticks

            [FieldOffset(800)] internal uint NodeId;      // PROCESS_NODE input
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ENUMADAPTERS2
        {
            internal uint NumAdapters;
            internal nint Adapters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ADAPTERINFO
        {
            internal uint Adapter;      // an open adapter handle, ours to close
            internal uint LuidLow;
            internal uint LuidHigh;
            internal uint NumOfSources;
            internal uint PrecisePresentRegionsPreferred;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_CLOSEADAPTER
        {
            internal uint Adapter;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYADAPTERINFO
        {
            internal uint Adapter;
            internal uint Type;
            internal nint PrivateDriverData;
            internal uint PrivateDriverDataSize;
        }

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTQueryStatistics(ref D3DKMT_QUERYSTATISTICS statistics);

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 adapters);

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER adapter);

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO info);
        #endregion
    }
}
