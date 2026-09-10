using MadWizard.Desomnia.Configuration;
using System.Diagnostics;
using MadWizard.Desomnia.Events;

// The parity harness (spec §9.1): written in phase 0 to characterize the LEGACY
// EventSource/Actor semantics, it accompanied the redesign through every phase and now
// pins the same contract on the finished engine — TestEvents keeps the legacy method
// names as thin wrappers so the pins read unchanged. The deliberate behavior changes
// (§9.3) flipped their pins phase by phase; QuirkTests is the historical marker.
//
// Scope notes:
// - The Parallel option is engine behavior, tested in EngineTests
//   (ParallelEventsAwaitAllEntriesConcurrently).
// - NetworkServiceWatch.CanTriggerDemand (NetworkServiceWatch.cs) is represented by
//   proxy through the HasHandlers-counts-bound-actions pin in WiringParityTests.

#pragma warning disable CS0067 // events are triggered via the framework's reflection, not directly

namespace MadWizard.Desomnia.Tests.Parity
{
    /// <summary>Base object exposing the (post-shim) engine surface to tests through the
    /// legacy method names, so the parity pins read unchanged.</summary>
    public class TestEvents : EventMetaObject
    {
        private readonly List<string> _log = [];
        private readonly Lock _logLock = new();

        public Event? LastActionEvent { get; private set; }

        public event EventInvocation? Alpha;
        public event EventInvocation? Beta;
        private event EventInvocation? Secret;

        public void Record(string entry) { lock (_logLock) _log.Add(entry); }
        public string[] Snapshot() { lock (_logLock) return [.. _log]; }
        public int Count(string entry) { lock (_logLock) return _log.Count(e => e == entry); }

        public void AddEventAction(string name, Configuration.ActionInfo? action)
        {
            if (action == null || action.Command is { } command && command.Function.Trim() == string.Empty)
                return;                          // the legacy null-before-lookup order (pinned)

            Event(name).AddAction(action);
        }

        public void AddEventHandler(string name, EventInvocation handler) => Event(name).AddHandler(handler);
        public bool HasEventHandlers(string name) => Event(name).HasHandlers;
        public void RemoveEventHandler(string name, EventInvocation handler) => Event(name).RemoveHandler(handler);

        public void DoTrigger(string name) => Event(name).TriggerEvent();
        public void DoTrigger(string name, Event e) => Event(name).TriggerEvent(e);
        public Task DoTriggerAsync(string name) => Event(name).TriggerEventAsync();
        public Task DoTriggerAsync(string name, Event e) => Event(name).TriggerEventAsync(e);
        public Task DoTriggerAsync(Event e) => Event(e.Type).TriggerEventAsync(e);
        public void DoCancel(string name) => Event(name).CancelActions();

        [ActionHandler("noop")]
        private void HandleNoop(Event e)
        {
            LastActionEvent = e;
            Record("noop");
        }

        [ActionHandler("noop2")]
        private void HandleNoop2() => Record("noop2");
    }

    /// <summary>Actor with [EventContext] properties (one non-public — the harvest scans
    /// NonPublic too, EventSource.cs:101) and an inherited-event surface.</summary>
    public class ContextActor : TestEvents
    {
        [EventContext]
        public string? Payload { get; set; }

        [EventContext]
        private Version? Hidden { get; set; }

        public void SetHidden(Version value) => Hidden = value;
    }

    public class TestToken : UsageToken { }

    public static class Wait
    {
        /// <summary>Polls until the condition holds; fails the test on timeout.</summary>
        public static async Task Until(Func<bool> condition, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();

            while (!condition())
            {
                if (watch.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"condition not met within {timeoutMs}ms");

                await Task.Delay(25);
            }
        }

        /// <summary>Grace period to prove something does NOT happen.</summary>
        public static Task Settle(int ms = 400) => Task.Delay(ms);

        /// <summary>Settle long enough to outlast a pending window of the given length.</summary>
        public static Task SettleAfter(int pendingDelayMs) => Task.Delay(pendingDelayMs + 500);
    }

    public static class Actions
    {
        public static ActionInfo Named(string name, params object[] args)
            => new(name, args.Length > 0 ? new Arguments(args) : null);

        /// <summary>The engine form — dispatch seams (TryHandleEventAction) no longer
        /// accept config ActionInfos.</summary>
        public static JSEventAction Command(string name, params object[] args)
            => new(name, args.Length > 0 ? args : null);

        public static ScheduledActionInfo Delayed(string name, int delayMs)
            => new(name, null, TimeSpan.FromMilliseconds(delayMs));

        public static ThrottledActionInfo Throttled(string name, uint times)
            => new(name, null, times);
    }
}
