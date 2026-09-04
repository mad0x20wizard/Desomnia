using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes
{
    public abstract class ProcessWatch(string? name) : Resource
    {
        /**
         * Mutated by whichever thread reports a process change – the poll loop, an ETW callback, a
         * kqueue notification, or the runtime's Exited event. Every path in and out locks the
         * roster; long-running process actions use a snapshot rather than hold that lock.
         */
        readonly Dictionary<int, IProcess> _watchedProcesses = [];

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

        public required ProcessMetricsWatch? MetricsWatch
        {
            private get; init
            {
                if ((field = value) is not null)
                {
                    lock (_watchedProcesses)
                    {
                        foreach (var process in _watchedProcesses.Values)
                        {
                            field.Track(process);
                        }
                    }
                }
            }
        }

        public ProcessWatchMetrics? Metrics => MetricsWatch?.Metrics;

        protected abstract bool ShouldWatchProcess(IProcess process);

        private bool WatchProcess(IProcess process)
        {
            if (_watchedProcesses.TryAdd(process.Id, process))
            {
                MetricsWatch?.Track(process);

                return true;
            }

            return false;
        }
        private bool UnWatchProcess(IProcess process)
        {
            if (_watchedProcesses.Remove(process.Id, out var watched))
            {
                MetricsWatch?.Untrack(watched);

                // remove any processes, that are not longer watched children
                while (_watchedProcesses.Values.FirstOrDefault(p => !ShouldWatchProcess(p)) is IProcess child)
                {
                    _watchedProcesses.Remove(child.Id);

                    MetricsWatch?.Untrack(child);
                }

                return true;
            }

            return false;
        }

        #region Inspection
        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            ProcessMetricsUsage? metrics = null;

            if (MetricsWatch is not null)
            {
                if ((metrics = MetricsWatch.TakeMeasurement(interval)) is null)
                {
                    yield break; // the specified metrics weren't satisfied
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
