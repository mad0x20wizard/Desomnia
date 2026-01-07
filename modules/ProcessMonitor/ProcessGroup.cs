using MadWizard.Desomnia.Process.Configuration;
using MadWizard.Desomnia.Process.Manager;


namespace MadWizard.Desomnia.Process
{
    public abstract class ProcessGroup(ProcessGroupInfo info) : Resource
    {
        private DateTime _lastMeasureTime;
        private TimeSpan _lastProcessorTime;

        protected abstract IEnumerable<IProcess> EnumerateProcesses();

        protected virtual ProcessUsage CreateUsageToken(double usage)
        {
            return new ProcessUsage(info.Name, usage);
        }

        protected IEnumerable<IProcess> SelectProcesses()
        {
            IEnumerable<IProcess> processes = EnumerateProcesses();

            var parents = new HashSet<IProcess>();
            foreach (var process in processes)
            {
                if (info.Pattern.Matches(process.Name).Count == 0)
                    continue;

                if (parents.Add(process))
                    yield return process;
            }

            if (info.WithChildren)
                foreach (var process in processes)
                {
                    if (parents.Contains(process))
                        continue;

                    foreach (var parent in parents)
                        if (process.HasParent(parent))
                            yield return process;
                }
        }

        private double MeasureUsage()
        {
            DateTime measureTime = DateTime.UtcNow;
            TimeSpan processorTime = SelectProcesses().Aggregate(TimeSpan.Zero, (time, process) => time + process.NativeProcess.TotalProcessorTime);

            try
            {
                var deltaProcessorTime = (processorTime - _lastProcessorTime).TotalMilliseconds;
                var deltaMeasureTime = (measureTime - _lastMeasureTime).TotalMilliseconds;

                return (deltaProcessorTime / (Environment.ProcessorCount * deltaMeasureTime)) * 100;
            }
            finally
            {
                _lastProcessorTime = processorTime;
                _lastMeasureTime = measureTime;
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var usage = MeasureUsage();

            if (usage > info.Threshold)
            {
                yield return CreateUsageToken(usage);
            }
        }

        [ActionHandler("stop")]
        internal void HandleActionStop()
        {
            foreach (var process in SelectProcesses())
                process.Stop();
        }
    }
}
