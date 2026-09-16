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

        public TraceEventProcessManager()
        {
            this.ListenerCountChanged += (sender, @event) => ConfigureSession();
        }

        private bool IsProcessing => _traceEventSession?.IsActive ?? false;

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

            // process lifetime and nothing else – the traffic meter runs its own session
            // (TraceEventTrafficListener), so metering demand never restarts this one and
            // no process start or stop is lost to a keyword change
            _traceEventSession = new("Desomnia::ProcessManager");
            _traceEventSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
            _traceEventSession.Source.Kernel.ProcessStart += ETW_ProcessStart;
            _traceEventSession.Source.Kernel.ProcessStop += ETW_ProcessStop;

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
                 * the pumping thread dies, so IsProcessing would stay true while no process event
                 * is ever delivered again – a roster frozen on its last diff, with every watch
                 * believing it. Torn down, the next enumeration falls back to a refresh and the
                 * next rebuild – or listener change – starts a fresh session; restarting from
                 * here would just spin if whatever killed the pump is not done killing.
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
            lock (this) // the same lock every other session mutator holds
            {
                UnsubscribeFromTraceEvents();
            }

            base.Dispose();
        }
    }
}
