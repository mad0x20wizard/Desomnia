using Autofac;
using Autofac.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace MadWizard.Desomnia.Processes.Manager
{
    public abstract class ProcessManager : IProcessManager, IStartable, IDisposable
    {
        public required ILogger Logger { protected get; init; }

        private bool _initialized = false;

        readonly ConcurrentDictionary<int, IProcess> _processList = [];

        public virtual event EventHandler<IProcess>? ProcessStarted;
        public virtual event EventHandler<IProcess>? ProcessStopped;

        /**
         * Creates the processes through the container, where everything a bare new could not
         * reach takes part: the logger arrives as a required property, the exit watch as a
         * middleware on the registration, and – when configured – the metric decorations wrap
         * the result. What remains here is only the seam the base class calls.
         */
        public required Func<ProcessInformation, IProcess?, IProcess> CreateProcess { private get; init; }

        public virtual void Start() => RefreshProcessList();

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

        protected virtual ProcessInformation? QueryParentProcess(ProcessInformation info) => info.ParentId;

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

        // createIfUnknown, because the indexer's callers ask about processes they positively
        // expect – above all a freshly launched one, which the platform's own event lane may
        // not have delivered yet; observers that must not create only what they saw mentioned
        // use TryFindProcess directly, without the flag
        public IProcess this[int pid] => TryFindProcess(pid, out IProcess? process, createIfUnknown: true) ? process : throw new ProcessNotFoundException(pid);

        public virtual IProcess LaunchProcess(ProcessStartInfo info)
        {
            var native = Process.Start(info) ?? throw new Exception("Process could not be started.");

            return TriggerStart(native)!;
        }

        public bool TryFindProcess(int pid, [NotNullWhen(true)] out IProcess? process, bool createIfUnknown = false)
        {
            if (!_processList.TryGetValue(pid, out process))
            {
                if (createIfUnknown && TriggerStart(pid) is IProcess created)
                {
                    return (process = created) is not null;
                }
            }
            else if (process?.HasStopped ?? false)
            {
                TriggerStop(process.Id);

                process = null;
            }

            return process != null;
        }

        /**
         * The .NET runtime doesn't provide a cross-platform abstraction for this.
         * Therefore the platform managers need to implement this via P/Invoke,
         * if possible – or report it straight from an enumeration that knows it anyway.
         */

        #region Internal Process Management
        protected IProcess? TriggerStart(ProcessInformation info)
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

                IProcess? parent;
                // pid 0 is nobody's parent, and a process that claims to be its own would loop forever
                if (info.MaxParents > 0 && (info.ParentId ?? QueryParentProcess(info)) is ProcessInformation infoParent
                    && infoParent.Id != 0 
                    && infoParent.Id != pid)
                {
                    if (!_processList.TryGetValue(infoParent.Id, out parent))
                    {
                        parent = TriggerStart(infoParent with { MaxParents = info.MaxParents - 1 });
                    }
                }
                else
                {
                    parent = null;
                }

                IProcess? process = null;
                if (_processList.GetOrAdd(pid, pid => process = CreateProcess(info, parent)) == process)
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

                if (process.Layer<ProcessHandle>() is { } handle)
                {
                    handle.TriggerStop(); // a no-op when this stop came from the process itself
                }

                ProcessStopped?.Invoke(this, process);
            }
        }
        #endregion

        public virtual IEnumerator<IProcess> GetEnumerator() => _processList.Values.GetEnumerator();

        public virtual void Dispose()
        {
            foreach (var process in this)
            {
                process.Dispose();
            }
        }
    }
}
