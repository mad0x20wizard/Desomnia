using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes
{
    public class ProcessWatch : Resource
    {
        readonly ProcessWatchInfo info;

        /**
         * Mutated by whichever thread reports a process change – the poll loop, an ETW callback, a
         * kqueue notification, the runtime's Exited event – while the inspection loop reads it.
         * Every path in and out locks the roster itself, and reads take a snapshot rather than hold
         * the lock while they work.
         *
         * A plain dictionary on purpose: what has to be atomic is not the change but the decision
         * riding on it – whether this add was the first or this removal the last. A concurrent
         * collection cannot answer that, and would only suggest it had.
         *
         * Keyed by pid, so a process leaves in one step rather than by a scan, and so the same
         * process cannot be held twice under two objects.
         *
         * The 'readonly' is load-bearing now that the dictionary is its own lock: reassign it and
         * two threads would be locking two different objects, with nothing to show for it. Compare
         * the measurement histories below, which are replaced wholesale every cycle.
         */
        readonly Dictionary<int, IProcess> _watchedProcesses = [];

        /// <summary>The watched processes as they were a moment ago; safe to walk while they change.</summary>
        private IProcess[] Processes
        {
            get
            {
                lock (_watchedProcesses)
                {
                    return [.. _watchedProcesses.Values];
                }
            }
        }

        // Construction time rather than default(DateTime): the first cycle's elapsed is then the
        // genuine first interval instead of seven hundred thousand days, which matters to every
        // rate threshold that divides by it.
        private DateTime _lastMeasureTime = DateTime.UtcNow;
        private Dictionary<IProcess, TimeSpan> _lastProcessorTime = [];
        private Dictionary<IProcess, ProcessInputOutput> _lastIO = [];
        private Dictionary<IProcess, ProcessInputOutput> _lastTraffic = [];

        private bool _warnedIO, _warnedTraffic;

        public required ILogger<ProcessWatch> Logger { private get; init; }

        public required IProcessManager Manager
        {
            private get; init
            {
                field = value;

                // always subscribe event before iterating
                field.ProcessStarted += Manager_ProcessStarted;
                field.ProcessStopped += Manager_ProcessStopped;

                lock (_watchedProcesses)
                {
                    foreach (var process in Manager.Where(ShouldWatchProcess))
                    {
                        _watchedProcesses.TryAdd(process.Id, process);
                    }
                }
            }
        }

        public event EventInvocation? Started;
        public event EventInvocation? Stopped;

        public ProcessWatch(ProcessWatchInfo info)
        {
            this.info = info;

            // NetworkWatch reads a unit-less threshold as raw packets, which processes cannot
            // count – and read as bytes, a naked number per interval would be satisfied by noise.
            // Better to refuse loudly at load than to guard a machine against 500 bytes.
            RequireByteUnit(info.MinIO, "minIO");
            RequireByteUnit(info.MinTraffic, "minTraffic");

            ((IEventSystem)this)[nameof(Idle)].AddAction(info.OnIdle);
            ((IEventSystem)this)[nameof(Demand)].AddAction(info.OnDemand);

            Started.AddAction(info.OnStart);
            Stopped.AddAction(info.OnStop);

            void RequireByteUnit(IOThreshold? threshold, string attribute)
            {
                if (threshold is IOThreshold t && t.TrafficUnit is null)
                    throw new FormatException($"'{info.Name}': {attribute} requires a byte unit (e.g. \"100kb\" or \"1MB/s\")");
            }
        }

        protected virtual bool ShouldWatchProcess(IProcess process)
        {
            if (info.IsFilePathPattern)
            {
                if (process.ImagePath is string path)
                {
                    if (info.Pattern.Count(path) > 0)
                        return true;
                }
            }
            else
            {
                if (info.Pattern.Count(process.Name) > 0)
                    return true;
            }

            if (info.WatchChildren)
            {
                lock (_watchedProcesses)
                {
                    foreach (var watched in _watchedProcesses.Values)
                        if (process.HasParent(watched))
                            return true;
                }
            }

            return false;
        }

        #region Inspection
        /**
         * The processor time the group consumed since the last measurement.
         *
         * Kept per process rather than as one group total, because the group is not a stable set:
         * a browser closing a tab used to subtract that process' entire lifetime from the sum, and
         * the group would report itself idle for a cycle while the rest of it was busy. A process
         * that has just joined has no previous reading and therefore contributes nothing yet – its
         * time before this interval was never ours to count.
         *
         * A process that has died between the manager's last poll and this inspection reports no
         * time at all. Its share of the interval is lost, but it must not abort the inspection:
         * the tokens of every resource behind it in the cycle would go with it.
         */
        private TimeSpan MeasureProcessorTime(IProcess[] processes)
        {
            var measured = new Dictionary<IProcess, TimeSpan>(_lastProcessorTime.Count);

            TimeSpan time = TimeSpan.Zero;

            foreach (var process in processes)
            {
                if (process.ProcessorTime is not TimeSpan total)
                    continue;

                if (_lastProcessorTime.TryGetValue(process, out TimeSpan last))
                    time += total - last;

                measured[process] = total;
            }

            _lastProcessorTime = measured; // whatever left the group takes its history with it

            return time;
        }

        /**
         * The bytes the group moved since the last measurement – the same per-process bookkeeping
         * as the processor time above, shared by both counter pairs, with two twists of its own:
         *
         * The deltas are clamped per field, because "monotonic" is a promise the platforms only
         * almost keep – the macOS write ledger is debited when dirtied pages are invalidated – and
         * one counter stepping backwards must dent this interval, not poison the whole sum with a
         * negative.
         *
         * And null means nobody could answer: distinct from a group that answered zero, it is the
         * signal the caller turns into "this platform cannot measure the threshold" – once, loudly.
         */
        private static long? MeasureBytes(IProcess[] processes, ref Dictionary<IProcess, ProcessInputOutput> history, Func<IProcess, ProcessInputOutput?> counters)
        {
            var measured = new Dictionary<IProcess, ProcessInputOutput>(history.Count);

            long bytes = 0;
            var sampled = false;

            foreach (var process in processes)
            {
                if (counters(process) is not ProcessInputOutput total)
                    continue;

                sampled = true;

                if (history.TryGetValue(process, out ProcessInputOutput last))
                {
                    bytes += Math.Max(0, total.BytesIn - last.BytesIn)
                           + Math.Max(0, total.BytesOut - last.BytesOut);
                }

                measured[process] = total;
            }

            history = measured;

            return sampled ? bytes : null;
        }

        /// There are difference between the platforms, in how the relative CPU usage is displayed:
        /// - The Windows Task-Manager shows CPU usage in relation to all the available multi-core CPU capacity.
        /// - The macOS Activity-Monitor shows CPU usage in relation to the single-core CPU capacity.
        ///
        /// In order to make it easier for the user to specify an approriate relative usage,
        /// we consider this difference when calculating the usage.
        private static double RelativeUsage(TimeSpan time, TimeSpan elapsed)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return time.TotalMilliseconds / (Environment.ProcessorCount * elapsed.TotalMilliseconds);
            }
            else
            {
                return time.TotalMilliseconds / (elapsed.TotalMilliseconds);
            }
        }

        /**
         * The comparison NetworkWatch applies to the same threshold type, minus its packet branch
         * (unit-less thresholds were refused in the constructor): with a time unit both sides
         * become per-millisecond rates, without one the interval's byte count is the value.
         * Inclusive, as on the network side – minCPU's strict '>' predates the shared type.
         */
        private static bool Satisfies(IOThreshold threshold, long bytes, TimeSpan elapsed)
        {
            double value = bytes;
            double minValue = threshold.Value * threshold.TrafficUnit!.Value;

            if (threshold.TimeUnit is TimeSpan time)
            {
                value /= elapsed.TotalMilliseconds;
                minValue /= time.TotalMilliseconds;
            }

            return value >= minValue;
        }

        /**
         * A threshold nobody can measure fails open, because the failure costs are asymmetric:
         * failing closed would let a sampling gap report the group idle – and onIdle can be 'stop',
         * so the worst case is not a wasted watt but a working process killed on a measurement
         * failure. Open, the watch degrades to whatever the other attributes still measure, and
         * says so once instead of every cycle.
         */
        private bool AssumeDemand(ref bool warned, string attribute)
        {
            if (!warned)
            {
                warned = true;

                Logger.LogWarning("'{Name}': no watched process could answer for {Attribute} – the threshold is ignored and demand assumed " +
                    "(unsupported platform, missing privileges, or metering not active)", info.Name, attribute);
            }

            return true;
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            // Without thresholds the mere existence of a process is the demand. Sampling counters
            // anyway costs a syscall per watched process, every cycle, for numbers nobody reads –
            // which is what made this module expensive where polling already is. The same rule
            // holds per attribute below: only a configured threshold is measured.
            if (!info.HasThresholds)
            {
                lock (_watchedProcesses)
                {
                    if (_watchedProcesses.Count > 0)
                    {
                        yield return new ProcessUsage(info.Name);
                    }
                }

                yield break;
            }

            var processes = Processes;

            var measureTime = DateTime.UtcNow;
            var elapsed = measureTime - _lastMeasureTime;
            _lastMeasureTime = measureTime;

            // an inspection forced within the same clock tick (InspectNow) must not divide by zero
            // – NaN compares false everywhere, which would silently read as "idle"
            if (elapsed <= TimeSpan.Zero)
                elapsed = TimeSpan.FromMilliseconds(1);

            TimeSpan time = TimeSpan.Zero;
            long? io = null, traffic = null;

            if (info.MinCPU is not null)
                time = MeasureProcessorTime(processes);
            if (info.MinIO is not null)
                io = MeasureBytes(processes, ref _lastIO, static process => process.StorageData);
            if (info.MinTraffic is not null)
                traffic = MeasureBytes(processes, ref _lastTraffic, static process => process.NetworkData);

            if (processes.Length == 0)
                yield break; // nothing to demand anything – and nothing to blame a null measurement on

            // every configured threshold has to hold: the min attributes chain with 'and'
            var demand = true;

            double? usage = null;
            TimeSpan? timeUsed = null;

            if (info.MinCPU is CPUThreshold cpu)
            {
                if (cpu.AbsoluteTime is TimeSpan minTime)
                {
                    demand &= time > minTime;

                    timeUsed = time;
                }
                else if (cpu.RelativeUsage is double minUsage)
                {
                    var relative = RelativeUsage(time, elapsed);

                    demand &= relative > minUsage;

                    usage = relative;
                }
            }

            if (info.MinIO is IOThreshold minIO)
            {
                demand &= io is long ioBytes ? Satisfies(minIO, ioBytes, elapsed) : AssumeDemand(ref _warnedIO, "minIO");
            }

            if (info.MinTraffic is IOThreshold minTraffic)
            {
                demand &= traffic is long trafficBytes ? Satisfies(minTraffic, trafficBytes, elapsed) : AssumeDemand(ref _warnedTraffic, "minTraffic");
            }

            if (demand)
            {
                yield return new ProcessUsage(info.Name) { Usage = usage, Time = timeUsed, Storage = io, Traffic = traffic };
            }
        }
        #endregion

        #region Process events
        private void Manager_ProcessStarted(object? sender, IProcess process)
        {
            if (ShouldWatchProcess(process))
            {
                lock (_watchedProcesses)
                    if (!_watchedProcesses.TryAdd(process.Id, process) || _watchedProcesses.Count > 1)
                        return;

                Started.TriggerEvent();
            }
        }

        private void Manager_ProcessStopped(object? sender, IProcess process)
        {
            lock (_watchedProcesses)
                if (!_watchedProcesses.Remove(process.Id) || _watchedProcesses.Count > 0)
                    return; // there are more processes to watch

            Stopped.TriggerEvent();
        }
        #endregion

        #region Action handlers
        [ActionHandler("stop")]
        internal async Task HandleActionStop(TimeSpan timeout = default) // TODO implement passing of timeout
        {
            foreach (var process in Processes)
            {
                await process.Stop(timeout);
            }
        }
        #endregion

        public override void Dispose()
        {
            Manager.ProcessStopped -= Manager_ProcessStopped;
            Manager.ProcessStarted -= Manager_ProcessStarted;

            base.Dispose();
        }
    }
}
