using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Process.Manager
{
    public class ProcessManager : IProcessManager
    {
        public required ILogger<ProcessManager> Logger { private get; init; }

        private delegate IEnumerable<IProcess> ProcessFilter(IEnumerable<IProcess> processes);

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

        public IEnumerable<IProcess> WithSessionId(uint sid)
        {
            return new ProcessManagerView(this) { Filter = processes => processes.Where(p => p.SessionId == sid) };
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

            public async Task Stop(TimeSpan? timeout = null)
            {
                // try gracefull shutdown
                process.CloseMainWindow();

                if (timeout.HasValue && timeout.Value.TotalMilliseconds > 0)
                {
                    try
                    {
                        await process.WaitForExitAsync(new CancellationTokenSource((int)timeout.Value.TotalMilliseconds).Token);
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

        private class ProcessManagerView(IProcessManager manager) : IIEnumerable<IProcess>
        {
            public required ProcessFilter Filter { private get; init; }

            public IEnumerator<IProcess> GetEnumerator()
            {
                return Filter(manager).GetEnumerator();
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
