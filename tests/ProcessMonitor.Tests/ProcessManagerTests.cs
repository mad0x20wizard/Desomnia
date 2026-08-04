using MadWizard.Desomnia.Processes.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using System.Diagnostics;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// The refresh diff and the enumeration seam a platform manager plugs into: ids come from
    /// <see cref="ProcessManager.EnumerateProcesses"/>, identity from
    /// <see cref="ProcessManager.QueryProcess"/> — and identity is asked for only about ids the
    /// diff finds genuinely new, which is the entire reason the two are separate calls.
    /// </summary>
    public class ProcessManagerTests
    {
        private static FakeProcessManager Manager(params (int Pid, string Name)[] processes)
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            foreach (var (pid, name) in processes)
                manager.Table[pid] = new(name, 0);

            return manager;
        }

        [Fact]
        public void FirstFill_AnnouncesNothing()
        {
            var manager = Manager((101, "chrome"), (102, "code"));

            var started = new List<IProcess>();
            manager.ProcessStarted += (sender, process) => started.Add(process);

            manager.Start();

            Assert.Equal(2, manager.Count());
            Assert.Empty(started); // everything already running at boot must not fire onStart
        }

        [Fact]
        public void ProcessAppearingAfterTheFirstFill_IsAnnounced()
        {
            var manager = Manager((101, "chrome"));

            manager.Start();

            var started = new List<IProcess>();
            manager.ProcessStarted += (sender, process) => started.Add(process);

            manager.Table[102] = new("code", 0);
            manager.Pump();

            Assert.Equal("code", Assert.Single(started).Name);
        }

        [Fact]
        public void ProcessLeavingTheEnumeration_IsAnnouncedStoppedOnce()
        {
            var manager = Manager((101, "chrome"), (102, "code"));

            manager.Start();

            var stopped = new List<IProcess>();
            manager.ProcessStopped += (sender, process) => stopped.Add(process);

            manager.Table.Remove(101);
            manager.Pump();
            manager.Pump(); // a second poll must not report it again

            Assert.Equal(102, Assert.Single(manager).Id);
            Assert.Equal("chrome", Assert.Single(stopped).Name);
        }

        [Fact]
        public void ProcessReportingItsOwnExit_IsAnnouncedWithoutWaitingForAPoll()
        {
            var manager = Manager((101, "chrome"), (102, "code"));

            manager.Start();

            var stopped = new List<IProcess>();
            manager.ProcessStopped += (sender, process) => stopped.Add(process);

            // what a kqueue NOTE_EXIT does on macOS, and the BCL's Exited event on Windows: the
            // process itself says it is gone, well before any enumeration would notice
            ((ProcessHandle)manager.Single(process => process.Id == 101)).TriggerStop();

            Assert.Equal("chrome", Assert.Single(stopped).Name);
            Assert.Equal(102, Assert.Single(manager).Id);
        }

        [Fact]
        public void ProcessThatReportedItsOwnExit_IsNotAnnouncedAgainByThePoll()
        {
            var manager = Manager((101, "chrome"), (102, "code"));

            manager.Start();

            var stopped = new List<IProcess>();
            manager.ProcessStopped += (sender, process) => stopped.Add(process);

            ((ProcessHandle)manager.Single(process => process.Id == 101)).TriggerStop();

            manager.Table.Remove(101); // the enumeration catches up a poll later
            manager.Pump();

            Assert.Single(stopped); // whichever lane was first is the only one that reports it
        }

        [Fact]
        public void ProcessStoppedByThePoll_DoesNotComeBackThroughItsOwnEvent()
        {
            var manager = Manager((101, "chrome"));

            manager.Start();

            var stopped = new List<IProcess>();
            manager.ProcessStopped += (sender, process) => stopped.Add(process);

            manager.Table.Remove(101);
            manager.Pump();

            Assert.Single(stopped);
        }

        [Fact]
        public void ProcessReportingItsOwnExit_RaisesItsOwnStoppedEventOnlyOnce()
        {
            var manager = Manager((101, "chrome"));

            manager.Start();

            var process = (ProcessHandle)Assert.Single(manager);

            var stopped = 0;
            process.Stopped += (sender, e) => stopped++;

            // The manager subscribes to this very event, and answers it by removing the process —
            // which comes straight back here. Anyone else listening (the session bridge watches its
            // minion this way) must still hear about the exit exactly once.
            process.TriggerStop();

            Assert.Equal(1, stopped);
        }

        [Fact]
        public void KnownProcess_IsNeverDescribedTwice()
        {
            var manager = Manager((101, "chrome"), (102, "code"));

            manager.Start();
            manager.Pump();
            manager.Pump();

            // the point of the seam: three polls, but identity was asked for exactly once per id
            Assert.Equal([101, 102], manager.Described.Order());
        }

        [Fact]
        public void DescribedProcess_IsNamedWithoutTheBcl()
        {
            var manager = Manager((101, "chrome"));

            manager.Start();

            var process = Assert.Single(manager);

            Assert.Equal("chrome", process.Name);
            Assert.Equal(101, process.Id);
            // touching Process here would go looking for pid 101 in the real world
        }

        [Fact]
        public void UndescribableProcess_IsSkipped()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            // The enumeration lists it, but by the time it is described it is gone – a zombie on
            // macOS, a pid that exited between the two calls anywhere. It is not a process.
            //
            // Deliberately a pid that certainly does exist: were the platform's "no" treated as
            // "ask the BCL instead", this id would be found and adopted, and skipping it here
            // would prove nothing but that the id was fictional.
            manager.Table[Environment.ProcessId] = new("ignored", 0);
            manager.Undescribable.Add(Environment.ProcessId);

            manager.Start();

            Assert.Empty(manager);
        }

        [Fact]
        public void EnumerationThatAlreadyNamesAProcess_IsNotDescribedAgain()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            // what the BCL path hands over: entries that arrive carrying their own identity
            manager.Table[101] = new("chrome", 0) { DescribedByEnumeration = true };

            manager.Start();

            Assert.Equal("chrome", Assert.Single(manager).Name);
            Assert.Empty(manager.Described); // asking again is the whole cost this seam exists to avoid
        }

        [Fact]
        public void UndescribableProcess_IsRetriedOnTheNextPoll()
        {
            var manager = Manager((101, "chrome"));

            manager.Undescribable.Add(101);
            manager.Start();

            manager.Undescribable.Clear(); // it settles down and can be described after all
            manager.Pump();

            Assert.Equal("chrome", Assert.Single(manager).Name);
        }

        [Fact]
        public void ManagerWithoutItsOwnDescription_FallsBackToTheBcl()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            manager.Table[Environment.ProcessId] = new("ignored", 0) { DeferToBase = true };

            manager.Start();

            using var self = Process.GetCurrentProcess();

            Assert.Equal(self.ProcessName, Assert.Single(manager).Name);
        }

        [Fact]
        public void ParentId_BuildsTheAncestorChain()
        {
            var manager = Manager((101, "code"));

            manager.Table[102] = new("node", 101);
            manager.Table[103] = new("esbuild", 102);

            manager.Start();

            var grandchild = manager.Single(process => process.Id == 103);
            var root = manager.Single(process => process.Id == 101);

            Assert.Equal(102, grandchild.Parent?.Id);
            Assert.True(grandchild.HasParent(root)); // what watchChildren walks
        }

        [Fact]
        public void ParentOutsideTheEnumeration_IsDescribedOnDemand()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            // the parent is describable but never listed – a process that exited while its child lives on
            manager.Table[101] = new("code", 0) { Listed = false };
            manager.Table[102] = new("node", 101);

            manager.Start();

            Assert.Equal("code", Assert.Single(manager, process => process.Id == 102).Parent?.Name);
        }

        [Fact]
        public void ProcessParentedByTheKernel_HasNoParent()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            manager.Table[1] = new("launchd", 0); // ppid 0 is the kernel, not a process

            manager.Start();

            Assert.Null(Assert.Single(manager).Parent);
        }

        [Fact]
        public void SelfParentingProcess_DoesNotRecurse()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            manager.Table[101] = new("liar", 101); // would otherwise recurse until the stack gives out

            manager.Start();

            Assert.Null(Assert.Single(manager).Parent);
            // the depth bound would also end up with a parentless process, sixty-four descriptions
            // later – it is the self-check that must catch this one, on the first look
            Assert.Equal([101], manager.Described);
        }

        [Fact]
        public void ParentLoop_TerminatesInsteadOfOverflowingTheStack()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            // no real kernel reports this – it reparents orphans instead – but a stack overflow is
            // not an exception anybody can catch, so the walk is bounded rather than trusted
            manager.Table[101] = new("a", 102);
            manager.Table[102] = new("b", 101) { Listed = false };

            manager.Start();

            Assert.NotNull(manager.Single(process => process.Id == 101));
        }

        /// <summary>
        /// The same bound as the loop above, but reached along a chain of distinct processes – so a
        /// walk that ignores it runs out of ancestors instead of out of stack, and says so as a
        /// failed assertion rather than by taking the test host down with it.
        ///
        /// Worth its own test because the bound travels in the description: every level asks the
        /// platform to describe an id, and a description arrives fresh, carrying the default depth.
        /// Adopting one wholesale hands the walk a full budget again, at every single level.
        /// </summary>
        [Fact]
        public void AncestryLongerThanTheBound_StopsClimbing()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            const int chain = 200;

            for (int pid = 1; pid <= chain; pid++)
                manager.Table[pid] = new($"p{pid}", pid < chain ? pid + 1 : 0) { Listed = pid == 1 };

            manager.Start();

            var depth = 0;
            for (var ancestor = manager.Single(process => process.Id == 1).Parent; ancestor != null; ancestor = ancestor.Parent)
                depth++;

            // The exact ceiling is ProcessInformation.MaxParents' business; that there is one is this.
            // Every ancestor the walk did reach is adopted, so the tracked count follows the depth –
            // unbounded, this manager would be holding all two hundred of them.
            Assert.InRange(depth, 1, 64);
            Assert.Equal(depth + 1, manager.Count());
        }

        /// <summary>
        /// A start reported for a process that is already tracked must not build a second one.
        ///
        /// Nothing about the duplicate is visible from the outside – the roster is keyed by pid, so it
        /// simply loses the add and is dropped. What it takes with it is the platform's exit watch,
        /// armed in its constructor: a kernel handle and a thread-pool wait on Windows, a pidfd on
        /// Linux, a kqueue registration on macOS, none of it given back until the process it names
        /// ends. Live, that read as three handles per process where the budget was one.
        /// </summary>
        [Fact]
        public void StartReportedTwice_BuildsOneProcess()
        {
            var manager = Manager((101, "chrome"));

            manager.Start();

            var started = new List<IProcess>();
            manager.ProcessStarted += (sender, process) => started.Add(process);

            var first = Assert.Single(manager);

            // what a trace event does for a process the enumeration already found
            manager.Announce(new ProcessInformation(101) { Name = "chrome", SessionId = 0 });
            manager.Announce(new ProcessInformation(101) { Name = "chrome", SessionId = 0 });

            Assert.Equal(1, manager.Created);            // built once, however often it is announced
            Assert.Empty(manager.Released);              // and nothing had to be given back
            Assert.Same(first, Assert.Single(manager));  // still the very same process object
            Assert.Empty(started);                       // and no second arrival announced
        }

        /// <summary>
        /// The same, for the race the check above cannot close: two lanes reporting one process at
        /// once. Only one can be tracked, and whatever the other built has to be handed back.
        /// </summary>
        [Fact]
        public void ProcessThatLosesTheRace_IsReleased()
        {
            var manager = new FakeProcessManager { Logger = NullLogger<ProcessManager>.Instance };

            manager.Table[102] = new("code", 0) { Listed = false };
            manager.Start();

            // The window is inside CreateProcess itself, which is the only place another lane can get
            // in between the check and the add – so that is where the other lane is let in.
            manager.RaceOnCreate = 102;

            manager.Announce(new ProcessInformation(102) { Name = "code", SessionId = 0 });

            Assert.Equal(2, manager.Created);                                    // both lanes built one
            Assert.Single(manager);                                              // one of them was kept
            Assert.Equal([102], manager.Released.Select(process => process.Id)); // the other handed back
        }

        /// <summary>
        /// A platform whose process table is a dictionary: <see cref="EnumerateProcesses"/> hands
        /// out bare ids the way a single syscall would, <see cref="QueryProcess"/> fills in the
        /// identity, and every describe call is recorded so a test can prove the refresh only asked
        /// about what it did not already know.
        /// </summary>
        private sealed class FakeProcessManager : ProcessManager
        {
            public Dictionary<int, TableEntry> Table { get; } = [];

            /// <summary>Ids the platform lists but finds nothing behind when it looks.</summary>
            public HashSet<int> Undescribable { get; } = [];

            public List<int> Described { get; } = [];

            /// <summary>How many processes this manager was asked to build, kept or not.</summary>
            public int Created { get; private set; }

            /// <summary>The ones it had to hand back – whatever creating them took out.</summary>
            public List<IProcess> Released { get; } = [];

            /// <summary>A pid to let another lane adopt while this one is still building its own.</summary>
            public int? RaceOnCreate { get; set; }

            public void Pump() => RefreshProcessList();

            /// <summary>What a trace event does: report a start, tracked already or not.</summary>
            public void Announce(ProcessInformation info) => TriggerStart(info);

            protected override IProcess CreateProcess(ProcessInformation info, IProcess? parent)
            {
                Created++;

                if (RaceOnCreate == info.Id)
                {
                    RaceOnCreate = null; // the other lane only gets in once

                    TriggerStart(info);
                }

                return base.CreateProcess(info, parent);
            }

            protected override void ReleaseProcess(IProcess process) => Released.Add(process);

            protected override IEnumerable<ProcessInformation> EnumerateProcesses()
            {
                return Table.Where(entry => entry.Value.Listed).Select(entry => entry.Value.DescribedByEnumeration
                    ? new ProcessInformation(entry.Key) { Name = entry.Value.Name, SessionId = 0, ParentId = entry.Value.Ppid }
                    : new ProcessInformation(entry.Key));
            }

            protected override ProcessInformation? QueryProcess(int pid)
            {
                Described.Add(pid);

                if (Undescribable.Contains(pid) || !Table.TryGetValue(pid, out TableEntry? entry))
                    return null;

                if (entry.DeferToBase)
                    return base.QueryProcess(pid); // a platform with no cheaper answer of its own

                return new ProcessInformation(pid)
                {
                    Name = entry.Name,
                    SessionId = 0,
                    ParentId = entry.Ppid,
                };
            }

            internal sealed record TableEntry(string Name, int Ppid)
            {
                /// <summary>False for a process the enumeration does not show but a child still points at.</summary>
                public bool Listed { get; init; } = true;

                /// <summary>True to hand identity over from the enumeration, as the BCL path does.</summary>
                public bool DescribedByEnumeration { get; init; }

                /// <summary>True to let the base class answer through the BCL, as an unported platform would.</summary>
                public bool DeferToBase { get; init; }
            }
        }
    }
}
