using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes
{
    internal class ProcessUsageMetricsWatch(ProcessWatchMetrics metrics)
    {
        // Construction time rather than default(DateTime): the first cycle's elapsed is then the
        // genuine first interval instead of seven hundred thousand days, which matters to every
        // rate threshold that divides by it.
        private DateTime _lastMeasureTime = DateTime.UtcNow;
        // keyed by whatever the clock belongs to – the process itself, or the group it shares one with
        private Dictionary<object, TimeSpan> _lastProcessorTime = [];
        private Dictionary<object, TimeSpan> _lastGraphicsTime = [];
        private Dictionary<IProcess, ProcessInputOutput> _lastIO = [];
        private Dictionary<IProcess, ProcessInputOutput> _lastTraffic = [];

        /**
         * Measures every configured threshold and reports what the group used – or null for a group
         * that did not reach the minimum, which is the caller's signal to stay quiet this cycle.
         *
         * Deliberately an array rather than a sequence: the four measurements below walk the group
         * once each, and a lazily-evaluated source would be re-enumerated – and re-sampled – for
         * every one of them.
         */
        internal ProcessUsageMetrics? TakeMeasurement(IProcess[] processes)
        {
            var measureTime = DateTime.UtcNow;
            var elapsed = measureTime - _lastMeasureTime;
            _lastMeasureTime = measureTime;

            // an inspection forced within the same clock tick (InspectNow) must not divide by zero
            // – NaN compares false everywhere, which would silently read as "idle"
            if (elapsed <= TimeSpan.Zero)
                elapsed = TimeSpan.FromMilliseconds(1);

            TimeSpan time = TimeSpan.Zero;
            TimeSpan? graphics = null;
            long? io = null, traffic = null;

            // minCPU keeps its established "no answer is zero" reading – the BCL clock answers for
            // every living process, so a group answering nothing is a group that died whole. The
            // graphics clock is different: entire platforms cannot answer it, and those must take
            // the fail-open path below rather than read as idle.
            if (metrics.MinCPU is not null)
                time = MeasureTime(processes, ref _lastProcessorTime, static process => process, static process => process.ProcessorTime) ?? TimeSpan.Zero;
            if (metrics.MinGPU is not null)
                graphics = MeasureTime(processes, ref _lastGraphicsTime, static process => process.GraphicsProcessorScope, static process => process.GraphicsProcessorTime);
            if (metrics.MinIO is not null)
                io = MeasureBytes(processes, ref _lastIO, static process => process.StorageData);
            if (metrics.MinTraffic is not null)
                traffic = MeasureBytes(processes, ref _lastTraffic, static process => process.NetworkData);

            if (processes.Length == 0)
                return null; // nothing to demand anything – and nothing to blame a null measurement on

            /**
             * How the min attributes chain. Under 'and' the group has to satisfy every one of them,
             * so the tally starts satisfied and each threshold may veto; under 'or' any single one
             * carries the group, so it starts unsatisfied and each threshold may rescue it.
             *
             * Which is also why a counter that answered for nobody this cycle is simply left out of
             * the tally: skipping it contributes the operator's identity – true under 'and', false
             * under 'or' – so it neither vetoes a group the other attributes found busy nor
             * declares one busy on no evidence.
             *
             * Note that this is no longer a platform question – a threshold the machine keeps no
             * counter for was refused when the watch was built (IProcessMetricSupport). What is
             * left is the momentary gap: a group whose processes all exited between the poll and
             * this cycle, or one nothing may open.
             */
            var all = metrics.Min == ProcessWatchMetrics.Operator.AND;

            var demand = all;
            var measured = false;

            void Compare(bool satisfied)
            {
                measured = true;

                demand = all ? demand & satisfied : demand | satisfied;
            }

            double? usage = null, graphicsUsage = null;
            TimeSpan? timeUsed = null, graphicsTimeUsed = null;

            if (metrics.MinCPU is ProcessingThreshold cpu)
            {
                if (cpu.AbsoluteTime is TimeSpan minTime)
                {
                    Compare(time > minTime);

                    timeUsed = time;
                }
                else if (cpu.RelativeUsage is double minUsage)
                {
                    var relative = RelativeUsage(time, elapsed);

                    Compare(relative > minUsage);

                    usage = relative;
                }
            }

            if (metrics.MinGPU is ProcessingThreshold gpu)
            {
                if (graphics is TimeSpan graphicsTime)
                {
                    if (gpu.AbsoluteTime is TimeSpan minTime)
                    {
                        Compare(graphicsTime > minTime);

                        graphicsTimeUsed = graphicsTime;
                    }
                    else if (gpu.RelativeUsage is double minUsage)
                    {
                        // no core-count analogue here: engines sum like cores, but no platform
                        // agrees on what an engine is, so the share is of the plain wall clock –
                        // and a multi-engine burst may honestly exceed 100%
                        var relative = graphicsTime.TotalMilliseconds / elapsed.TotalMilliseconds;

                        Compare(relative > minUsage);

                        graphicsUsage = relative;
                    }
                }
            }

            if (metrics.MinIO is TransmissionThreshold minIO && io is long ioBytes)
            {
                Compare(Satisfies(minIO, ioBytes, elapsed));
            }

            if (metrics.MinTraffic is TransmissionThreshold minTraffic && traffic is long trafficBytes)
            {
                Compare(Satisfies(minTraffic, trafficBytes, elapsed));
            }

            /**
             * Not one threshold could be measured, so there is no tally to believe either way and
             * the identity above would have answered on the operator alone. Demand is assumed
             * instead, because the failure costs are asymmetric: reading a measurement gap as idle
             * would let onIdle – which can be 'stop' – kill a working process over a counter that
             * was merely unreadable this cycle. Open, it costs a wasted watt.
             */
            if (!measured)
            {
                demand = true;
            }

            if (demand)
            {
                // a rate threshold measured a rate and an absolute one an amount – the token
                // carries what was actually compared, so the usage log reads in the user's unit
                var ioRate = metrics.MinIO?.TimeUnit is not null;
                var trafficRate = metrics.MinTraffic?.TimeUnit is not null;

                return new()
                {
                    ProcessingUsage = usage,
                    ProcessingTime = timeUsed,

                    GraphicsProcessingUsage = graphicsUsage,
                    GraphicsProcessingTime = graphicsTimeUsed,

                    Storage = ioRate ? null : io,
                    StorageRate = ioRate ? io / elapsed.TotalSeconds : null,

                    Traffic = trafficRate ? null : traffic,
                    TrafficRate = trafficRate ? traffic / elapsed.TotalSeconds : null,
                };
            }

            return null;
        }

        /**
         * The processing time the group consumed since the last measurement – one bookkeeping for
         * both clocks, the CPU's and the GPU's.
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
         *
         * The deltas are clamped like the byte counters' below – the graphics clock restarts when
         * the driver does, and one process stepping backwards must dent this interval, not poison
         * the whole sum with a negative. And null means nobody could answer: distinct from a group
         * that answered zero, and turned into fail-open where a whole platform has no clock.
         *
         * Bookkeeping is per *scope* rather than strictly per process, because a clock need not be
         * kept per process: macOS bills graphics time to a coalition, so an app and the helpers it
         * spawned all read the same counter, and a watch holding several of them would otherwise
         * count that one interval once per member. Where a platform does account per process every
         * process is its own scope and this is the bookkeeping it always was.
         */
        private static TimeSpan? MeasureTime(IProcess[] processes, ref Dictionary<object, TimeSpan> history, Func<IProcess, object> scope, Func<IProcess, TimeSpan?> clock)
        {
            var measured = new Dictionary<object, TimeSpan>(history.Count);

            TimeSpan time = TimeSpan.Zero;
            var sampled = false;

            foreach (var process in processes)
            {
                var key = scope(process);

                if (measured.ContainsKey(key))
                    continue; // a clock already read through another process sharing it

                if (clock(process) is not TimeSpan total)
                    continue;

                sampled = true;

                if (history.TryGetValue(key, out TimeSpan last) && total > last)
                    time += total - last;

                measured[key] = total;
            }

            history = measured; // whatever left the group takes its history with it

            return sampled ? time : null;
        }

        /**
         * The bytes the group moved since the last measurement – the same per-process bookkeeping
         * as the processing time above, shared by both counter pairs: deltas clamped per field,
         * because "monotonic" is a promise the platforms only almost keep – the macOS write ledger
         * is debited when dirtied pages are invalidated – and null when nobody could answer, the
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
        private static bool Satisfies(TransmissionThreshold threshold, long bytes, TimeSpan elapsed)
        {
            double value = bytes;
            double minValue = threshold.Amount * threshold.ByteUnit!.Value;

            if (threshold.TimeUnit is TimeSpan time)
            {
                value /= elapsed.TotalMilliseconds;
                minValue /= time.TotalMilliseconds;
            }

            return value >= minValue;
        }
    }
}
