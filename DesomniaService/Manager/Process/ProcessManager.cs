using Autofac;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Process.Manager
{
    public partial class ProcessManager : IProcessManager, IStartable, IDisposable
    {
        public required ILogger<ProcessManager> Logger { private get; init; }

        public event EventHandler<IProcess>? ProcessStarted;
        public event EventHandler<IProcess>? ProcessStopped;

        private TraceEventSession? _traceEventSession;

        public IProcess LaunchProcess(ProcessStartInfo info)
        {
            return new ProcessExt(System.Diagnostics.Process.Start(info) ?? throw new Exception("Process could not be started."));
        }

        void IStartable.Start()
        {
            _traceEventSession = new("Desomnia::ProcessManager");
            _traceEventSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

            _traceEventSession.Source.Kernel.ProcessStart += ETW_ProcessStart;
            _traceEventSession.Source.Kernel.ProcessStop += ETW_ProcessStop;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    _traceEventSession.Source.Process();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "unexpected ETW processing failure");
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void ETW_ProcessStart(ProcessTraceData data)
        {
            //Logger.LogTrace("Process started: {name} ({id})", data.ProcessName, data.ProcessID);

            ProcessStarted?.Invoke(this, new ProcessExt(data.ProcessID));
        }

        private void ETW_ProcessStop(ProcessTraceData data)
        {
            //Logger.LogTrace("Process stopped: {name} ({id})", data.ProcessName, data.ProcessID);

            ProcessStopped?.Invoke(this, new ProcessExt(data.ProcessID));
        }

        void IDisposable.Dispose()
        {
            _traceEventSession?.Source.StopProcessing();
            _traceEventSession?.Stop();
            _traceEventSession?.Dispose();
            _traceEventSession = null;
        }

        public IEnumerator<IProcess> GetEnumerator()
        {
            var processList = new List<IProcess>();

            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                var p = new ProcessExt(process) { Cache = processList };

                processList.Add(p);
            }

            return processList.GetEnumerator();
        }

        internal class ProcessExt(System.Diagnostics.Process process) : IProcess
        {
            internal ProcessExt(int id) : this(System.Diagnostics.Process.GetProcessById(id)) { }

            public int Id => process.Id;
            public int SessionId => process.SessionId;
            public string Name => process.ProcessName;

            public System.Diagnostics.Process NativeProcess => process;

            public IEnumerable<IProcess>? Cache { private get; init; }

            public IProcess? Parent
            {
                get
                {
                    var id = GetParentProcessId(this.NativeProcess);

                    if (id != null)
                    {
                        if (Cache != null)
                        {
                            return Cache.Where(proc => proc.Id == id).FirstOrDefault();
                        }

                        return new ProcessExt((int)id);
                    }

                    return null;
                }
            }

            public async Task Stop(TimeSpan timeout = default)
            {
                // try gracefull shutdown
                process.CloseMainWindow();

                if (timeout.TotalMilliseconds > 0)
                {
                    try
                    {
                        await process.WaitForExitAsync(new CancellationTokenSource((int)timeout.TotalMilliseconds).Token);
                    }
                    catch (TimeoutException)
                    {
                        // too late
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill(); // kill it anyway
                }
            }
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
