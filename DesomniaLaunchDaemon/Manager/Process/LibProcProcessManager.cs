using MadWizard.Desomnia.LaunchDaemon.Configuration;
using MadWizard.Desomnia.LaunchDaemon.Native;
using MadWizard.Desomnia.Processes.Manager.Metrics;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>
    /// macOS has no unprivileged way to be *told* that a process started — Endpoint Security would
    /// know, but only for software Apple has granted the entitlement to. So this still polls; what
    /// it does not do is ask the BCL, whose macOS enumeration builds a full ProcessInfo for every
    /// process on the machine, walking each of its threads, to answer a question about ids.
    ///
    /// Here a poll is <see cref="LibProc.EnumeratePIDs"/> and nothing else: one syscall, one int
    /// array. Only ids the diff finds genuinely new are described, and describing one costs three
    /// calls — which is also where the parent comes from, the thing the BCL has no cross-platform
    /// way to report, and therefore why <c>watchChildren</c> works on this platform at all.
    /// </summary>
    internal sealed class LibProcProcessManager : PollingProcessManager, IProcessMetricSupport
    {
        private KQueueProcessExitWatcher? _watcher;
        private bool _reportedGraphicsMeasurement;
        private readonly GraphicsMeasurementMode _configuredGraphics;
        private readonly GraphicsMeasurementMethod _graphicsMeasurement;

        public ProcessMetric SupportedMetrics =>
            ProcessMetric.Processor | ProcessMetric.Storage |
            (_graphicsMeasurement == GraphicsMeasurementMethod.None ? ProcessMetric.None : ProcessMetric.Graphics);

        public ProcessMetric SharedMetrics =>
            _graphicsMeasurement == GraphicsMeasurementMethod.Coalition ? ProcessMetric.Graphics : ProcessMetric.None;

        public LibProcProcessManager(
            TimeSpan interval,
            GraphicsMeasurementMode configuredGraphics,
            GraphicsMeasurementMethod graphicsMeasurement) : base(interval)
        {
            _configuredGraphics = configuredGraphics;
            _graphicsMeasurement = graphicsMeasurement;

            // the kernel is only asked to report
            // anything while somebody is actually listening for it
            ListenerCountChanged += (sender, count) => ConfigureWatcher();
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            ReportGraphicsMeasurement();

            return base.StartAsync(cancellationToken);
        }

        private void ReportGraphicsMeasurement()
        {
            if (_reportedGraphicsMeasurement)
                return;

            _reportedGraphicsMeasurement = true;

            if (_graphicsMeasurement == GraphicsMeasurementMethod.None)
            {
                Logger.LogWarning("GPU measurement mode {configured} is unavailable; process GPU metrics are not supported", _configuredGraphics);
            }
            else if (_configuredGraphics == GraphicsMeasurementMode.Automatic)
            {
                Logger.LogInformation("GPU measurement automatically selected {measurement}", _graphicsMeasurement);
            }
            else
            {
                Logger.LogInformation("GPU measurement selected {measurement}", _graphicsMeasurement);
            }
        }

        private void ConfigureWatcher()
        {
            lock (this)
            {
                if (ListenerCount == 0)
                {
                    _watcher?.Dispose();
                    _watcher = null;

                    return;
                }

                if (_watcher != null)
                    return;

                try
                {
                    _watcher = new KQueueProcessExitWatcher(Logger);
                }
                catch (Exception ex)
                {
                    // no worse than before it existed: exits are then noticed by the next poll
                    Logger.LogWarning(ex, "Falling back to polling for process exits");

                    return;
                }

                // catch up on everything already tracked (the enumeration guard sees this lock and
                // will not start a refresh underneath us); roster entries may carry a decoration
                // around the platform's process, so the concrete layer is unwrapped here
                foreach (var process in this)
                {
                    WatchForExit(process.Layer<LibProcProcess>());
                }
            }
        }

        protected override IEnumerable<ProcessInformation> EnumerateProcesses()
        {
            return LibProc.EnumeratePIDs().Select(pid => new ProcessInformation(pid));
        }

        protected override ProcessInformation? QueryProcess(int pid)
        {
            if (LibProc.GetProcessInfo(pid) is not LibProc.proc_bsdshortinfo info)
                return null; // a zombie the list still carries, pid 0, or simply gone again

            return new ProcessInformation(pid)
            {
                // The executable name, which is what the BCL reports on this platform too, and the
                // only form long enough for a pattern like "com.apple.WebKit.WebContent" to match.
                // The kernel's command name is the fallback, and it stops after 15 characters.
                Name = Path.GetFileName(LibProc.GetProcessPath(pid)) is { Length: > 0 } name ? name : info.GetCommand(),
                SessionId = LibProc.GetSessionId(pid),
                // launchd's parent is the kernel, which is not a process anybody can watch
                ParentId = info.pbsi_ppid > 0 && info.pbsi_ppid != (uint)pid ? (int)info.pbsi_ppid : null,
            };
        }

        // internal for the creation middleware, which reports what it just activated – always the
        // concrete process, because the middleware runs before any decoration wraps it
        internal void WatchForExit(LibProcProcess process)
        {
            if (_watcher is KQueueProcessExitWatcher watcher)
            {
                watcher.Watch(process.Id, process.TriggerStop);
            }
        }

        public override void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;

            base.Dispose();
        }
    }
}
