using Autofac;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

using NativeProcess = System.Diagnostics.Process;

namespace MadWizard.Desomnia.Process.Manager
{
    public abstract class ProcessManager : IProcessManager, IStartable
    {
        public required ILogger<ProcessManager> Logger { protected get; init; }

        private bool _initialized = false;

        readonly ConcurrentDictionary<int, IProcess> _processList = [];

        public virtual event EventHandler<IProcess>? ProcessStarted;
        public virtual event EventHandler<IProcess>? ProcessStopped;

        public virtual void Start() => RefreshProcessList();

        protected virtual void RefreshProcessList()
        {
            lock (this)
            {
                var watch = Stopwatch.StartNew();

                var snapshot = NativeProcess.GetProcesses();

                var minus = 0;
                // remove stopped processes
                foreach (var process in this.Where(p => !snapshot.Any(s => s.Id == p.Id)))
                {
                    TriggerStop(process.Id); minus++;
                }

                var plus = 0;
                // add started processes
                foreach (var native in snapshot.Where(s => !this.Any(p => p.Id == s.Id)))
                {
                    TriggerStart(native); plus++;
                }

                Logger.LogTrace("Refreshed process list: +{plus}/-{minus} -> {count} [{time} ms]", plus, minus, this.Count(), watch.ElapsedMilliseconds);

                _initialized |= true;
            }
        }

        public IProcess this[int pid]
        {
            get
            {
                if (!_processList.TryGetValue((int)pid, out IProcess? process))
                {
                    if (TriggerStart(pid: pid) is IProcess created)
                    {
                        return created;
                    }
                }
                else if (process?.NativeProcess.HasExited ?? false)
                {
                    TriggerStop(process.Id);

                    process = null;
                }

                return process ?? throw new KeyNotFoundException("Process with pid = " + pid + " not found");
            }
        }

        public virtual IProcess LaunchProcess(ProcessStartInfo info)
        {
            var native = NativeProcess.Start(info) ?? throw new Exception("Process could not be started.");

            return TriggerStart(native)!;
        }

        /**
         * The .NET runtime doesn't provide a cross-platform abstraction for this.
         * Therefore the platform managers need to implement this via P/Invoke,
         * if possible.
         */
        protected virtual int? FindParentProcessId(NativeProcess p) => null;

        #region Internal Process Management
        protected IProcess? TriggerStart(NativeProcess? native = null, int pid = default)
        {
            try
            {
                native ??= NativeProcess.GetProcessById(pid); pid = native.Id;

                IProcess? parent;
                if (FindParentProcessId(native) is int pidParent)
                {
                    if (_processList.TryGetValue(pidParent, out parent))
                    {
                        parent = TriggerStart(pid: pidParent);
                    }
                }
                else
                {
                    parent = null;
                }

                IProcess process;
                if (_processList.TryAdd(pid, process = new ProcessWrapper(native) { Parent = parent }))
                {
                    if (_initialized)
                    {
                        Logger.LogTrace("Process '{name}' ({pid}) started", process.Name, process.Id);

                        ProcessStarted?.Invoke(this, process);
                    }
                }

                return _processList[pid];
            }
            catch (SystemException ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Logger.LogTrace(ex.Message); // probably not running (any more)

                return null;
            }
        }

        protected void TriggerStop(int pid)
        {
            if (_processList.TryRemove(pid, out IProcess? process))
            {
                Logger.LogTrace("Process '{name}' ({pid}) stopped", process.Name, process.Id);

                if (process is ProcessWrapper wrapper)
                {
                    wrapper.TriggerStop();
                }

                ProcessStopped?.Invoke(this, process);
            }
        }
        #endregion

        public virtual IEnumerator<IProcess> GetEnumerator() => _processList.Values.GetEnumerator();
    }
}
