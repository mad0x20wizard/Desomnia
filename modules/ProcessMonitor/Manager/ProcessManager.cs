using Autofac.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace MadWizard.Desomnia.Processes.Manager
{
    public abstract class ProcessManager : IProcessManager, IDisposable
    {
        public required ILogger Logger { protected get; init; }

        private bool _initialized = false;

        readonly ConcurrentDictionary<int, IProcess> _processList = [];

        public virtual event EventHandler<IProcess>? ProcessStarted;
        public virtual event EventHandler<IProcess>? ProcessStopped;

        /**
         * Creates the processes through the container, where everything a bare new could not
         * reach takes part: the logger arrives as a required property, the exit watch and the
         * parent resolver as middleware on the registration, and the platform's metric
         * decorations wrap the result. What remains here is only the seam the base class calls.
         */
        public required Func<ProcessInformation, IProcess> CreateProcess { private get; init; }

        /**
         * The processes alive right now – the ids are all the refresh below actually compares.
         *
         * The default answer comes from the BCL, which is one kernel call on Windows but, on Unix,
         * builds a complete ProcessInfo for every process – walking every one of its threads – just
         * to hand back an id. Platform managers that can list ids with a single syscall override
         * this and yield bare entries, leaving identity to QueryProcess.
         */
        protected virtual IEnumerable<ProcessInformation> EnumerateProcesses()
        {
            return Process.GetProcesses().Select(p => new ProcessInformation(p));
        }

        /**
         * Describes a process the enumeration did not hand over already: a parent discovered through
         * its child, an id an indexer lookup was asked about, or – for managers whose enumeration
         * yields bare ids – every newly appeared process.
         *
         * The default is the BCL lookup, which is what an enumeration of System.Diagnostics.Process
         * objects would have produced anyway. A platform that can describe an id more cheaply
         * overrides this and answers entirely on its own terms – including null, which then means
         * the platform looked and there was nothing there (an id that has since exited, or a zombie
         * the enumeration still lists), not "ask somebody else".
         */
        protected virtual ProcessInformation? QueryProcess(int pid) => new(Process.GetProcessById(pid));

        /**
         * The parent's description, asked by the parent resolver for a process whose own
         * description did not name one already. The .NET runtime doesn't provide a
         * cross-platform abstraction for this, so the platform managers answer via P/Invoke
         * where they can – or report it straight from an enumeration that knows it anyway,
         * which is what the base falls back to.
         */
        protected internal virtual ProcessInformation? QueryParentProcess(ProcessInformation info) => info.ParentId;

        protected virtual void RefreshProcessList()
        {
            lock (this)
            {
                var watch = Stopwatch.StartNew();

                // Both sides of the diff are keyed by pid. This used to be two nested LINQ scans,
                // which on a machine with a few hundred processes spent more time comparing the
                // list than the OS spent producing it – ConcurrentDictionary copies all of its
                // values on every enumeration, and the inner scan asked for them n times.
                var snapshot = new Dictionary<int, ProcessInformation>();

                foreach (var entry in EnumerateProcesses())
                {
                    snapshot[entry.Id] = entry;
                }

                var minus = 0;
                // remove stopped processes ('Keys' hands out a snapshot, so removing while we walk it is safe)
                foreach (var pid in _processList.Keys)
                {
                    if (!snapshot.ContainsKey(pid))
                    {
                        TriggerStop(pid); minus++;
                    }
                }

                var plus = 0;
                // add started processes – an id the platform declines to describe is simply not one
                // (a zombie the enumeration still lists, or a process that exited in between)
                foreach (var entry in snapshot.Values)
                {
                    if (!_processList.ContainsKey(entry.Id) && TriggerStart(entry) != null)
                    {
                        plus++;
                    }
                }

                // ConcurrentDictionary.Count takes every one of its internal bucket locks, so the
                // total is not a free number to log – and the arguments of a LogTrace call are
                // evaluated whether or not anybody is listening at that level
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.LogTrace("Refreshed process list: +{plus}/-{minus} -> {count} [{time} ms]", plus, minus, _processList.Count, watch.ElapsedMilliseconds);
                }

                _initialized |= true;
            }
        }

        public IProcess this[int pid] => TryFindProcess(pid, out IProcess? process, true, true) ? process : throw new ProcessNotFoundException(pid);

        public bool TryFindProcess(int pid, [NotNullWhen(true)] out IProcess? process, bool createIfUnknown = false, bool checkIfStopped = false)
        {
            if (!_processList.TryGetValue(pid, out process))
            {
                if (createIfUnknown && TriggerStart(pid) is IProcess created)
                {
                    return (process = created) is not null;
                }
            }
            else if (checkIfStopped && (process?.HasStopped ?? false))
            {
                TriggerStop(process.Id);

                process = null;
            }

            return process != null;
        }

        public virtual IProcess LaunchProcess(ProcessStartInfo info)
        {
            var native = Process.Start(info) ?? throw new Exception("Process could not be started.");

            return TriggerStart(native)!;
        }

        #region Internal Process Management
        protected internal IProcess? TriggerStart(ProcessInformation info)
        {
            try
            {
                int pid = info.Id;

                if (info.Name is null) // we need the name
                {
                    if (QueryProcess(info.Id) is not ProcessInformation queried)
                        return null;

                    // Everything the platform just told us, but not how far we still are allowed to
                    // climb: a description is minted fresh and carries the default depth, so adopting
                    // it wholesale would hand every level of an ancestry a full budget again – and the
                    // walk below is the one thing that budget exists to bound.
                    info = queried with { MaxParents = info.MaxParents };
                }

                IProcess? process = null;
                if (_processList.GetOrAdd(pid, pid => process = CreateProcess(info)) == process)
                {
                    // A platform may learn that this process is gone long before the next enumeration
                    // would: Windows by waiting on the process handle, macOS through kqueue, Linux
                    // through a pidfd. Whichever lane reports first, the manager takes it as its own —
                    // so a stop is announced the moment it happens rather than at the next poll.
                    process.Stopped += (sender, @event) => TriggerStop(process.Id);

                    if (_initialized)
                    {
                        Logger.LogTrace("Process '{name}' ({pid}) started", process.Name, process.Id);

                        ProcessStarted?.Invoke(this, process);
                    }
                }
                else
                {
                    // built, and beaten to the roster by another lane: the duplicate is handed
                    // back with everything creating it took out – an exit watch, a kernel handle
                    process?.Dispose();
                    process = null;
                }

                return process ?? _processList.GetValueOrDefault(pid);
            }
            catch (KeyNotFoundException)
            {
                return null; // stopped directly after it started
            }
            catch (SystemException ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Logger.LogTrace(ex.Message); // probably not running (any more)

                return null;
            }
            catch (DependencyResolutionException ex) when (ex.GetBaseException() is ArgumentException or InvalidOperationException)
            {
                // the same "probably not running" – but thrown inside the container the processes
                // are created through now, which wraps it beyond the filter above
                Logger.LogTrace(ex.GetBaseException().Message);

                return null;
            }
        }

        protected void TriggerStop(int pid)
        {
            if (_processList.TryRemove(pid, out IProcess? process))
            {
                Logger.LogTrace("Process '{name}' ({pid}) stopped", process.Name, process.Id);

                // if parent dies, remove it from all children
                foreach (var orphaned in _processList.Values.Where(p => p.Parent == process))
                {
                    orphaned.Layer<ProcessHandle>()?.Parent = null;
                }

                process.Layer<ProcessHandle>()?.TriggerStop();  // a no-op when this stop came from the process itself

                ProcessStopped?.Invoke(this, process);
            }
        }
        #endregion

        /**
         * The roster is built on first use, never at activation: a build where nothing ever asks
         * for processes never enumerates the machine, and never arms a single exit watch – and
         * because no process is created inside the manager's own activation anymore, the creation
         * middlewares may resolve the manager back from the container. The guard is for the
         * catch-up walks the platform watchers do under the manager's own lock: those must see
         * the roster as it is, not start a refresh underneath themselves.
         */
        public virtual IEnumerator<IProcess> GetEnumerator()
        {
            if (!_initialized && !Monitor.IsEntered(this))
                RefreshProcessList();

            return _processList.Values.GetEnumerator();
        }

        public virtual void Dispose()
        {
            if (_initialized)
            {
                foreach (var process in this)
                {
                    process.Dispose();
                }
            }
        }
    }
}
