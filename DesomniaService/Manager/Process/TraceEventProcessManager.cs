using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.InteropServices;

using NativeProcess = System.Diagnostics.Process;

/**
 * Uses ETW (Event Trace for Windows) to get process start/stop notifications in near realtime.
 */
namespace MadWizard.Desomnia.Process.Manager
{
    public partial class TraceEventProcessManager : ListenerAwareProcessManager, IDisposable
    {
        TraceEventSession? _traceEventSession;

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

            _traceEventSession = new("Desomnia::ProcessManager");
            _traceEventSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
            _traceEventSession.Source.Kernel.ProcessStart += ETW_ProcessStart;
            _traceEventSession.Source.Kernel.ProcessStop += ETW_ProcessStop;

            Task.Factory.StartNew(ETW_Process, TaskCreationOptions.LongRunning);
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

        protected override int? FindParentProcessId(NativeProcess process)
        {
            try
            {
                var info = new PROCESS_BASIC_INFORMATION();

                try
                {
                    if (NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
                        throw new Win32Exception();
                }
                catch (Win32Exception e) when (e.NativeErrorCode == 5)
                {
                    return null; // Zugriff verweigert
                }

                return info.InheritedFromUniqueProcessId.ToInt32();
            }
            catch (SystemException e) when (e is ArgumentException || e is InvalidOperationException)
            {
                return null;
            }
        }

        #region ETW callbacks
        private void ETW_Process()
        {
            try
            {
                _traceEventSession!.Source.Process();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_Process"); // TODO maybe try to restart processing?
            }
        }

        private void ETW_ProcessStart(ProcessTraceData data)
        {
            try
            {
                TriggerStart(pid: data.ProcessID);
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

        public void Dispose()
        {
            Logger.LogDebug("Shutting down...");

            UnsubscribeFromTraceEvents();
        }

        #region Windows-API
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryInformationProcess(nint processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            internal nint Reserved1;
            internal nint PebBaseAddress;
            internal nint Reserved2_0;
            internal nint Reserved2_1;
            internal nint UniqueProcessId;
            internal nint InheritedFromUniqueProcessId;
        }
        #endregion
    }
}
