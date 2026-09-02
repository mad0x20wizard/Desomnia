using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes
{
    public abstract class ProcessWatch(string? name) : Resource
    {
        readonly ProcessUsageMetricsWatch? _metricsWatch;

        /**
         * Mutated by whichever thread reports a process change – the poll loop, an ETW callback, a
         * kqueue notification, the runtime's Exited event – while the inspection loop reads it.
         * Every path in and out locks the roster itself, and reads take a snapshot rather than hold
         * the lock while they work.
         */
        protected readonly Dictionary<int, IProcess> _watchedProcesses = [];

        protected ProcessWatch(ProcessWatchMetrics metrics, string? name = null) : this(name)
        {
            if (metrics.HasThresholds)
            {
                _metricsWatch = new(metrics);
            }
        }

        public string? Name => name;

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
                        WatchProcess(process);
                    }
                }
            }
        }

        protected abstract bool ShouldWatchProcess(IProcess process);

        private bool WatchProcess(IProcess process)
        {
            lock (_watchedProcesses)
            {
                if (_watchedProcesses.TryAdd(process.Id, process))
                {
                    _metricsWatch?.Track(process);

                    return true;
                }
            }

            return false;
        }

        private bool UnWatchProcess(IProcess process)
        {
            lock (_watchedProcesses)
            {
                if (_watchedProcesses.Remove(process.Id))
                {
                    // remove any processes, that are not longer watched children
                    while (_watchedProcesses.Values.FirstOrDefault(p => !ShouldWatchProcess(p)) is IProcess child)
                    {
                        _watchedProcesses.Remove(child.Id);
                    }

                    return true;
                }
            }

            return false;
        }

        #region Inspection
        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            ProcessUsageMetrics? metrics = null;

            if (_metricsWatch is not null)
            {
                if ((metrics = _metricsWatch.TakeMeasurement(TakeSnapshot(), interval)) is null)
                {
                    yield break; // didn't satisfy the metrics minimum
                }
            }

            lock (_watchedProcesses)
            {
                if (_watchedProcesses.Count > 0)
                {
                    yield return new ProcessUsage(name) { Metrics = metrics };
                }
            }
        }
        #endregion

        #region Process start/stop events
        public event EventInvocation? Started;
        public event EventInvocation? Stopped;

        private void Manager_ProcessStarted(object? sender, IProcess process)
        {
            if (ShouldWatchProcess(process))
            {
                lock (_watchedProcesses)
                    if (!WatchProcess(process) || _watchedProcesses.Count > 1)
                        return;

                Started.TriggerEvent();
            }
        }

        private void Manager_ProcessStopped(object? sender, IProcess process)
        {
            lock (_watchedProcesses)
                if (!UnWatchProcess(process) || _watchedProcesses.Count > 0)
                    return; // there are more processes to watch

            Stopped.TriggerEvent();
        }
        #endregion

        #region Action handlers
        [ActionHandler("stop")]
        internal async Task HandleActionStop(TimeSpan timeout = default) // TODO implement passing of timeout
        {
            foreach (var process in TakeSnapshot())
            {
                await process.Stop(timeout);
            }
        }
        #endregion

        /// <summary>The watched processes as they were a moment ago; safe to walk while they change.</summary>
        private IProcess[] TakeSnapshot()
        {
            lock (_watchedProcesses)
            {
                return [.. _watchedProcesses.Values];
            }
        }

        public override void Dispose()
        {
            Manager.ProcessStopped -= Manager_ProcessStopped;
            Manager.ProcessStarted -= Manager_ProcessStarted;

            base.Dispose();
        }
    }
}
