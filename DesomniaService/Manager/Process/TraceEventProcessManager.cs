using MadWizard.Desomnia.Processes.Manager.Metrics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>Uses ETW (Event Trace for Windows) to get process start/stop notifications in near realtime.</summary>
    public class TraceEventProcessManager : Win32ProcessManager, IDisposable
    {
        TraceEventSession? _traceEventSession;

        /**
         * The metrics riding this session – registered by configuration, empty by default. Each
         * declares the kernel keywords it needs, reads its events off the shared source, and
         * books what it measures into the process objects itself; the manager never asks one
         * anything back, so a new per-process measurement is a new registration, never another
         * change here. The session's flags are the union of what the metrics ask for, fixed at
         * enable time.
         */
        public IEnumerable<ITraceEventMetric> Metrics { private get; init; } = [];

        public TraceEventProcessManager()
        {
            this.ListenerCountChanged += (sender, @event) => ConfigureSession();
        }

        private bool IsProcessing => _traceEventSession?.IsActive ?? false;

        public override void Start() => ConfigureSession();

        private void ConfigureSession()
        {
            lock (this)
            {
                if (IsProcessing)
                {
                    if (ListenerCount == 0)
                    {
                        UnsubscribeFromTraceEvents();
                    }
                }
                else
                {
                    if (ListenerCount > 0)
                    {
                        SubscribeToTraceEvents();

                        RefreshProcessList();
                    }
                }
            }
        }

        private void SubscribeToTraceEvents()
        {
            Logger.LogDebug("Subscribing to process trace events...");

            var keywords = Metrics.Select(m => m.Keywords).Aggregate(KernelTraceEventParser.Keywords.Process, (keywords, k) => keywords |= k);

            _traceEventSession = new("Desomnia::ProcessManager");
            _traceEventSession.EnableKernelProvider(keywords);
            _traceEventSession.Source.Kernel.ProcessStart += ETW_ProcessStart;
            _traceEventSession.Source.Kernel.ProcessStop += ETW_ProcessStop;

            foreach (var metric in Metrics)
            {
                // reset on the way in, not only on the way out: the teardown never joins the pump
                // thread, so a handler caught mid-flight can write after the stop-side Reset – but
                // never after this one, because nothing pumps before Process() below
                metric.Reset();

                metric.Subscribe(_traceEventSession.Source.Kernel);
            }

            // The session is captured rather than re-read from the field: a stop/start cycle can
            // replace the field before this task's thread ever runs, and a stale task adopting
            // the new session would race the new task's pump on a single-threaded dispatcher.
            var session = _traceEventSession;

            Task.Factory.StartNew(() => ETW_Process(session), TaskCreationOptions.LongRunning);
        }

        private void UnsubscribeFromTraceEvents()
        {
            if (_traceEventSession != null)
            {
                _traceEventSession.Source.StopProcessing();
                _traceEventSession.Stop();

                _traceEventSession.Dispose();
                _traceEventSession = null;

                foreach (var metric in Metrics)
                {
                    metric.Reset();
                }

                Logger.LogDebug("Unsubscribed from process trace events");
            }
        }

        #region ETW Process callbacks
        private void ETW_Process(TraceEventSession session)
        {
            try
            {
                session.Source.Process();
            }
            catch (ObjectDisposedException)
            {
                // superseded by a restart before this thread ever ran; the replacement has its own pump
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_Process"); // TODO maybe try to restart processing?

                /**
                 * A dead pump must take its session with it. The OS keeps the session alive after
                 * the pumping thread dies, so IsProcessing would stay true while nothing updates a
                 * counter again – and a frozen counter is a 0-byte delta, which reads as a group
                 * gone idle: the one lie the metering must never tell (a stopped session answers
                 * null instead, and the watch assumes demand). Torn down, the next rebuild – or a
                 * listener change – starts a fresh session; restarting from here would just spin
                 * if whatever killed the pump is not done killing.
                 */
                lock (this)
                {
                    if (_traceEventSession == session)
                    {
                        UnsubscribeFromTraceEvents();
                    }
                }
            }
        }

        private void ETW_ProcessStart(ProcessTraceData data)
        {
            try
            {
                TriggerStart(new(data.ProcessID)
                {
                    Name = data.ProcessName,
                    ParentId = data.ParentID,
                    SessionId = data.SessionID,
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_ProcessStart");
            }
        }
        private void ETW_ProcessStop(ProcessTraceData data)
        {
            try
            {
                TriggerStop(data.ProcessID);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_ProcessStop");
            }
        }
        #endregion

        public override IEnumerator<IProcess> GetEnumerator()
        {
            if (!IsProcessing)
                RefreshProcessList();

            return base.GetEnumerator();
        }

        public override void Dispose()
        {
            Logger.LogDebug("Shutting down...");

            lock (this) // the same lock every other session mutator holds
            {
                UnsubscribeFromTraceEvents();
            }

            base.Dispose();
        }
    }
}
