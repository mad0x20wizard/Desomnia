using Autofac.Features.Decorators;
using MadWizard.Desomnia.LaunchDaemon.Native;
using System.Diagnostics;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /// <summary>
    /// Per-process graphics accounting from AGXDeviceUserClient IORegistry entries. A registry
    /// walk answers for the whole machine, so all decorators in an inspection share one frozen
    /// snapshot; reads made outside an inspection share a short-lived live snapshot instead.
    /// </summary>
    internal sealed class AGXGraphicsProcess(IProcess process, IDecoratorContext context) : ProcessDecorator(process, context)
    {
        private static readonly Lock Gate = new();
        private static readonly TimeSpan Freshness = TimeSpan.FromMilliseconds(250);

        private static Snapshot? _latest;
        private static long _latestTimestamp;

        [ThreadStatic]
        private static Snapshot? _inspection;

        [ThreadStatic]
        private static bool _inspecting;

        [ThreadStatic]
        private static bool _inspectionRead;

        public override TimeSpan? GraphicsProcessorTime => Read(Id);

        /// <summary>Probes the actual schema and primes the first live snapshot.</summary>
        internal static bool Probe()
        {
            lock (Gate)
            {
                if (!AGXDeviceUserClients.TryTakeSnapshot(out Snapshot? snapshot, out bool compatible))
                    return false;

                _latest = snapshot;
                _latestTimestamp = Stopwatch.GetTimestamp();

                return compatible;
            }
        }

        internal static IDisposable BeginInspection()
        {
            _inspection = null;
            _inspectionRead = false;
            _inspecting = true;

            return new InspectionScope(() =>
            {
                _inspection = null;
                _inspectionRead = false;
                _inspecting = false;
            });
        }

        private static TimeSpan? Read(int pid)
        {
            if (_inspecting)
            {
                if (!_inspectionRead)
                {
                    _inspection = AGXDeviceUserClients.TryTakeSnapshot(out Snapshot? snapshot, out _) ? snapshot : null;
                    _inspectionRead = true;
                }

                return _inspection?.TimeOf(pid);
            }

            lock (Gate)
            {
                var now = Stopwatch.GetTimestamp();

                if (_latest is null || Stopwatch.GetElapsedTime(_latestTimestamp, now) >= Freshness)
                {
                    _latest = AGXDeviceUserClients.TryTakeSnapshot(out Snapshot? snapshot, out _) ? snapshot : null;
                    _latestTimestamp = now;
                }

                return _latest?.TimeOf(pid);
            }
        }

        internal sealed class Snapshot(Dictionary<int, TimeSpan> times)
        {
            /// <summary>A successful machine snapshot means absence is an honest zero.</summary>
            internal TimeSpan TimeOf(int pid) => times.GetValueOrDefault(pid);
        }

        private sealed class InspectionScope(Action close) : IDisposable
        {
            private Action? _close = close;

            public void Dispose() => Interlocked.Exchange(ref _close, null)?.Invoke();
        }

        /// <summary>The isolated native query behind this decorator.</summary>
        private static class AGXDeviceUserClients
        {
            private const string ServicePlane = "IOService";

            internal static bool TryTakeSnapshot(out Snapshot? snapshot, out bool compatible)
            {
                var totals = new Dictionary<int, ulong>();
                var visited = new HashSet<ulong>();
                var roots = VisitAccelerators("IOAccelerator", visited, totals, out int creators, out int counters);

                if (roots == 0)
                    roots = VisitAccelerators("AGXAccelerator", visited, totals, out creators, out counters);

                compatible = roots > 0 && creators > 0 && counters > 0;

                if (roots == 0 || creators == 0)
                {
                    snapshot = null;

                    return false;
                }

                snapshot = new(totals.ToDictionary(entry => entry.Key, entry => FromNanoseconds(entry.Value)));

                return true;
            }

            private static int VisitAccelerators(
                string className,
                HashSet<ulong> visited,
                Dictionary<int, ulong> totals,
                out int creators,
                out int counters)
            {
                var roots = 0;
                creators = counters = 0;

                foreach (uint accelerator in IOKit.FindServices(className))
                {
                    roots++;

                    try
                    {
                        VisitChildren(accelerator, visited, totals, ref creators, ref counters);
                    }
                    finally
                    {
                        IOKit.IOObjectRelease(accelerator);
                    }
                }

                return roots;
            }

            private static void VisitChildren(
                uint parent,
                HashSet<ulong> visited,
                Dictionary<int, ulong> totals,
                ref int creators,
                ref int counters)
            {
                if (IOKit.IORegistryEntryGetChildIterator(parent, ServicePlane, out uint iterator) != 0)
                    return;

                try
                {
                    while (IOKit.IOIteratorNext(iterator) is uint child && child != 0)
                    {
                        try
                        {
                            var identified = IOKit.IORegistryEntryGetRegistryEntryID(child, out ulong id) == 0;

                            if (identified && !visited.Add(id))
                                continue;

                            if (IOKit.IOObjectConformsTo(child, "AGXDeviceUserClient") != 0)
                                ReadClient(child, totals, ref creators, ref counters);

                            VisitChildren(child, visited, totals, ref creators, ref counters);
                        }
                        finally
                        {
                            IOKit.IOObjectRelease(child);
                        }
                    }
                }
                finally
                {
                    IOKit.IOObjectRelease(iterator);
                }
            }

            private static void ReadClient(uint client, Dictionary<int, ulong> totals, ref int creators, ref int counters)
            {
                if (ParseProcessId(IOKit.GetStringProperty(client, "IOUserClientCreator")) is not int pid)
                    return;

                creators++;

                nint usage = IOKit.GetProperty(client, "AppUsage");

                if (usage == 0)
                    return;

                try
                {
                    if (CF.IsArray(usage))
                    {
                        for (nint i = 0; i < CF.CFArrayGetCount(usage); i++)
                            AddUsage(CF.CFArrayGetValueAtIndex(usage, i), pid, totals, ref counters);
                    }
                    else
                    {
                        AddUsage(usage, pid, totals, ref counters);
                    }
                }
                finally
                {
                    CF.CFRelease(usage);
                }
            }

            private static void AddUsage(nint usage, int pid, Dictionary<int, ulong> totals, ref int counters)
            {
                if (!CF.IsDictionary(usage) || CF.GetNumber(usage, "accumulatedGPUTime") is not long value || value < 0)
                    return;

                counters++;

                ulong total = totals.GetValueOrDefault(pid);
                ulong addition = (ulong)value;

                totals[pid] = ulong.MaxValue - total < addition ? ulong.MaxValue : total + addition;
            }

            private static int? ParseProcessId(string? creator)
            {
                if (creator is null)
                    return null;

                int marker = creator.IndexOf("pid ", StringComparison.OrdinalIgnoreCase);

                if (marker < 0)
                    return null;

                ReadOnlySpan<char> digits = creator.AsSpan(marker + 4);
                var length = 0;

                while (length < digits.Length && char.IsAsciiDigit(digits[length]))
                    length++;

                return length > 0 && int.TryParse(digits[..length], out int pid) ? pid : null;
            }

            private static TimeSpan FromNanoseconds(ulong nanoseconds)
            {
                ulong ticks = nanoseconds / 100;

                return TimeSpan.FromTicks(ticks > long.MaxValue ? long.MaxValue : (long)ticks);
            }
        }
    }
}
