using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
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
         * _lastProcessorTime below, which is replaced wholesale every cycle.
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

        private DateTime _lastMeasureTime;
        private Dictionary<IProcess, TimeSpan> _lastProcessorTime = [];

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

            ((IEventSystem)this)[nameof(Idle)].AddAction(info.OnIdle);
            ((IEventSystem)this)[nameof(Demand)].AddAction(info.OnDemand);

            Started.AddAction(info.OnStart);
            Stopped.AddAction(info.OnStop);
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
        private double MeasureUsage(out TimeSpan time)
        {
            DateTime measureTime = DateTime.UtcNow;

            var measured = new Dictionary<IProcess, TimeSpan>(_lastProcessorTime.Count);

            time = TimeSpan.Zero;

            foreach (var process in Processes)
            {
                if (process.ProcessorTime is not TimeSpan total)
                    continue;

                if (_lastProcessorTime.TryGetValue(process, out TimeSpan last))
                    time += total - last;

                measured[process] = total;
            }

            try
            {
                var timeElapsed = (measureTime - _lastMeasureTime);

                /// There are difference between the platforms, in how the relative CPU usage is displayed:
                /// - The Windows Task-Manager shows CPU usage in relation to all the available multi-core CPU capacity.
                /// - The macOS Activity-Monitor shows CPU usage in relation to the single-core CPU capacity.
                /// 
                /// In order to make it easier for the user to specify an approriate relative usage,
                /// we consider this difference when calculating the usage.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return time.TotalMilliseconds / (Environment.ProcessorCount * timeElapsed.TotalMilliseconds);
                }
                else
                {
                    return time.TotalMilliseconds / (timeElapsed.TotalMilliseconds);
                }
            }
            finally
            {
                _lastProcessorTime = measured; // whatever left the group takes its history with it
                _lastMeasureTime = measureTime;
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            // Without a threshold the mere existence of a process is the demand. Sampling the
            // processor time anyway costs a syscall per watched process, every cycle, for a number
            // nobody reads – which is what made this module expensive where polling already is.
            if (info.MinCPU is not CPUThreshold threshold)
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

            var usage = MeasureUsage(out TimeSpan time);

            if (threshold.AbsoluteTime is TimeSpan minTime)
            {
                if (time > minTime)
                {
                    yield return new ProcessUsage(info.Name, time);
                }
            }
            else if (threshold.RelativeUsage is double minUsage)
            {
                if (usage > minUsage)
                {
                    yield return new ProcessUsage(info.Name, usage);
                }
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
