using Autofac;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Process.Manager
{
    public partial class ProcessManager : IProcessManager, IStartable, IDisposable
    {
        public required ILogger<ProcessManager> Logger { private get; init; }

        private TraceEventSession _traceEventSession = new("Desomnia::ProcessManager");

        readonly ConcurrentDictionary<int, IProcess> _processes = [];

        public event EventHandler<IProcess>? ProcessStarted;
        public event EventHandler<IProcess>? ProcessStopped;

        IProcess IProcessManager.LaunchProcess(ProcessStartInfo info)
        {
            var proc = System.Diagnostics.Process.Start(info) ?? throw new Exception("Process could not be started.");

            return RememberProcess(proc)!;
        }

        void IStartable.Start()
        {
            Task.Factory.StartNew(ETW_Process, TaskCreationOptions.LongRunning);

            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                RememberProcess(pid: p.Id);
            }
        }

        #region Event Tracing for Windows callbacks
        private void ETW_Process()
        {
            try
            {
                _traceEventSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
                _traceEventSession.Source.Kernel.ProcessStart += ETW_ProcessStart;
                _traceEventSession.Source.Kernel.ProcessStop += ETW_ProcessStop;

                _traceEventSession!.Source.Process();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_Process"); // TODO maybe try to restart processing?
            }
        }

        private void ETW_ProcessStart(ProcessTraceData data)
        {
            Logger.LogTrace("Process started: {name} ({id})", data.ProcessName, data.ProcessID);

            try
            {
                if (RememberProcess(pid: data.ProcessID) is IProcess process)
                {
                    ProcessStarted?.Invoke(this, process);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_ProcessStart");
            }
        }

        private void ETW_ProcessStop(ProcessTraceData data)
        {
            Logger.LogTrace("Process stopped: {name} ({id})", data.ProcessName, data.ProcessID);

            try
            {
                if (ForgetProcess(data.ProcessID) is IProcess process)
                {
                    ProcessStopped?.Invoke(this, process);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ETW_ProcessStop");
            }
        }
        #endregion

        #region Internal Process Management
        internal IProcess? RememberProcess(System.Diagnostics.Process? process = null, int pid = default)
        {
            try
            {
                process ??= System.Diagnostics.Process.GetProcessById(pid);

                pid = process.Id;

                IProcess? parent = null;
                if (GetParentProcessId(process) is int parentId)
                {
                    parent = RememberProcess(pid: parentId);
                }

                _processes.TryAdd(pid, new ProcessWrapper(process) { Parent = parent });

                return _processes[pid];
            }
            catch (ArgumentException ex)
            {
                Logger.LogTrace(ex.Message); // probably not running (any more)

                return null;
            }
        }

        private IProcess? ForgetProcess(int pid)
        {
            try
            {
                _processes.TryRemove(pid, out IProcess? process);

                return process;
            }
            catch (ArgumentException ex)
            {
                Logger.LogTrace(ex.Message); // probably not running (any more)

                return null;
            }
        }
        #endregion

        public IEnumerator<IProcess> GetEnumerator()
        {
            return _processes.Values.GetEnumerator();
        }

        void IDisposable.Dispose()
        {
            _traceEventSession?.Source.StopProcessing();
            _traceEventSession?.Stop();
            _traceEventSession?.Dispose();
            _traceEventSession = null;
        }

        #region Windows-API
        private static int? GetParentProcessId(System.Diagnostics.Process process)
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
