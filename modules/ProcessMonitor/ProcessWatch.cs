using MadWizard.Desomnia.Process.Configuration;
using MadWizard.Desomnia.Process.Manager;


namespace MadWizard.Desomnia.Process
{
    public class ProcessWatch : Resource
    {
        readonly ProcessWatchInfo info;

        readonly HashSet<IProcess> _watchedProcesses = [];

        private DateTime _lastMeasureTime;
        private TimeSpan _lastProcessorTime;

        public required IProcessManager Manager
        {
            private get; init
            {
                field = value;

                foreach (var process in Manager.Where(WatchProcess))
                {
                    _watchedProcesses.Add(process);
                }

                field.ProcessStarted += Manager_ProcessStarted;
                field.ProcessStopped += Manager_ProcessStopped;
            }
        }

        public event EventInvocation? Started;
        public event EventInvocation? Stopped;

        public ProcessWatch(ProcessWatchInfo info)
        {
            this.info = info;

            AddEventAction(nameof(Started), info.OnStart);
            AddEventAction(nameof(Stopped), info.OnStop);
        }

        protected virtual bool WatchProcess(IProcess process)
        {
            if (info.Pattern.Matches(process.Name).Count > 0)
                return true;

            if (info.WatchChildren)
            {
                foreach (var watched in _watchedProcesses)
                    if (process.HasParent(watched))
                        return true;
            }

            return false;
        }

        #region Inspection
        private double MeasureUsage(out TimeSpan time)
        {
            DateTime measureTime = DateTime.UtcNow;

            var processorTime = _watchedProcesses.Aggregate(TimeSpan.Zero, (time, process) => time + process.NativeProcess.TotalProcessorTime);

            try
            {
                time = (processorTime - _lastProcessorTime);
                var timeElapsed = (measureTime - _lastMeasureTime);

                return time.TotalMilliseconds / (Environment.ProcessorCount * timeElapsed.TotalMilliseconds);
            }
            finally
            {
                _lastProcessorTime = processorTime;
                _lastMeasureTime = measureTime;
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var usage = MeasureUsage(out TimeSpan time);

            if (info.MinCPU.AbsoluteTime is TimeSpan minTime)
            {
                if (time > minTime)
                {
                    yield return new ProcessUsage(info.Name, time);
                }
            }
            else if (info.MinCPU.RelativeUsage is double minUsage)
            {
                if (usage > minUsage)
                {
                    yield return new ProcessUsage(info.Name, usage);
                }
            }
            else if (_watchedProcesses.Count > 0)
            {
                yield return new ProcessUsage(info.Name);
            }
        }
        #endregion

        #region Process events
        private void Manager_ProcessStarted(object? sender, IProcess process)
        {
            if (WatchProcess(process))
            {
                var stopped = _watchedProcesses.Count == 0;

                _watchedProcesses.Add(process);

                if (stopped)
                {
                    TriggerEvent(nameof(Started));
                }
            }
        }

        private void Manager_ProcessStopped(object? sender, IProcess process)
        {
            if (_watchedProcesses.Where(watched => watched.Id == process.Id).FirstOrDefault() is IProcess stopped)
            {
                _watchedProcesses.Remove(stopped);

                if (_watchedProcesses.Count == 0)
                {
                    TriggerEvent(nameof(Stopped));
                }
            }
        }
        #endregion

        #region Action handlers
        [ActionHandler("stop")]
        internal void HandleActionStop(TimeSpan timeout = default) // TODO implement passing of timeout
        {
            foreach (var process in _watchedProcesses)
            {
                process.Stop(timeout);
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
