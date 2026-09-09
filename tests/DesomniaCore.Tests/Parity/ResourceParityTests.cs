using MadWizard.Desomnia.Events;
using Xunit;

namespace MadWizard.Desomnia.Tests.Parity
{
    /// <summary>§9.1: Resource semantics — Idle/Demand opposite-cancellation on inspection
    /// transitions, IsIdle tracking, and action/error bubbling to tracking monitors
    /// (Resource.cs:33-59, 89-113): synchronous, recursive, first-match, self-first.</summary>
    public class ResourceParityTests
    {
        private const int Window = 750;

        private class TestResource : Resource
        {
            public UsageToken[] Tokens = [];

            protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval) => Tokens;

            public readonly List<string> Log = [];

            [ActionHandler("mark")]
            private void Mark() => Log.Add("mark");
        }

        /// <summary>No InspectResource override — the base enumeration over tracked
        /// inspectables (ResourceMonitor.cs:65-71) must stay live for tracking tests.</summary>
        private class CatchAllMonitor : ResourceMonitor<IInspectable>
        {
            public readonly List<string> Caught = [];

            [ActionHandler("bubbled")]
            private void Bubbled() => Caught.Add("bubbled");

            protected override bool OnActionError(ActionError error)
            {
                Caught.Add($"error:{error.Exception?.GetType().Name}");
                return true;
            }
        }

        private class MarkMonitor : ResourceMonitor<IInspectable>
        {
            public readonly List<string> Log = [];

            [ActionHandler("mark")]
            private void Mark() => Log.Add("monitor-mark");
        }

        [Fact]
        public void InspectTogglesIsIdleByTokenPresence()
        {
            var resource = new TestResource();

            Assert.True(resource.IsIdle);

            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);
            Assert.False(resource.IsIdle);

            resource.Tokens = [];
            resource.Inspect(TimeSpan.Zero);
            Assert.True(resource.IsIdle);
        }

        [Fact]
        public async Task DemandCancelsPendingIdleAction()
        {
            var resource = new TestResource();
            ((IEventSystem)resource)["Idle"].AddAction(Actions.Delayed("mark", Window));

            resource.Inspect(TimeSpan.Zero);                 // idle → arms "mark"
            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);                 // demand → cancels the pending idle action

            await Wait.SettleAfter(Window);
            Assert.Empty(resource.Log);
        }

        [Fact]
        public async Task IdleCancelsPendingDemandAction()
        {
            var resource = new TestResource { Tokens = [new TestToken()] };
            ((IEventSystem)resource)["Demand"].AddAction(Actions.Delayed("mark", Window));

            resource.Inspect(TimeSpan.Zero);                 // demand → arms "mark"
            resource.Tokens = [];
            resource.Inspect(TimeSpan.Zero);                 // idle → cancels the pending demand action

            await Wait.SettleAfter(Window);
            Assert.Empty(resource.Log);
        }

        private class VetoingResource : Resource
        {
            public UsageToken[] Tokens = [];
            public bool Veto;
            public readonly List<string> Log = [];

            protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval) => Tokens;

            protected override bool ShouldTriggerEvent(Event @event)
            {
                return !Veto && base.ShouldTriggerEvent(@event);
            }

            [ActionHandler("mark")]
            private void Mark() => Log.Add("mark");
        }

        [Fact]
        public async Task VetoedEventCancelsNothing()
        {
            // flipped quirk (phase 3, §6.1/§9.3): opposite-cancellation is pipeline-
            // enforced via [EventOpposite] — a vetoed Idle never reaches the pipeline,
            // so Demand's pending survives and fires (the old hand-coded cancel ran
            // BEFORE the veto could intervene)
            var resource = new VetoingResource { Tokens = [new TestToken()] };
            ((IEventSystem)resource)["Demand"].AddAction(Actions.Delayed("mark", 150));

            resource.Inspect(TimeSpan.Zero);                 // demand → arms "mark"

            resource.Veto = true;
            resource.Tokens = [];
            resource.Inspect(TimeSpan.Zero);                 // idle VETOED → cancels nothing

            await Wait.Until(() => resource.Log.Count == 1); // the pending fires
        }

        [Fact]
        public async Task VetoControl_UnvetoedIdleStillCancelsThePending()
        {
            var resource = new VetoingResource { Tokens = [new TestToken()] };
            ((IEventSystem)resource)["Demand"].AddAction(Actions.Delayed("mark", Window));

            resource.Inspect(TimeSpan.Zero);                 // demand → arms

            resource.Tokens = [];
            resource.Inspect(TimeSpan.Zero);                 // idle NOT vetoed → annotation cancels

            await Wait.SettleAfter(Window);
            Assert.Empty(resource.Log);
        }

        [Fact]
        public async Task PendingIdleActionFiresWhenNoDemandIntervenes()
        {
            var resource = new TestResource();
            ((IEventSystem)resource)["Idle"].AddAction(Actions.Delayed("mark", 100));

            resource.Inspect(TimeSpan.Zero);

            await Wait.Until(() => resource.Log.Count == 1);
        }

        [Fact]
        public void UnhandledActionBubblesToTrackingMonitorSynchronously()
        {
            // bubbling happens inside the blocking trigger (Resource.cs:93-95) —
            // observable immediately after Inspect returns, no polling
            var resource = new TestResource();
            var monitor = new CatchAllMonitor();
            monitor.StartTracking(resource);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("bubbled"));   // only the monitor has it
            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(["bubbled"], monitor.Caught);
        }

        [Fact]
        public void OwnHandlerPrecedesBubbling()
        {
            var resource = new TestResource();
            var monitor = new MarkMonitor();
            monitor.StartTracking(resource);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("mark"));      // both declare "mark"
            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(["mark"], resource.Log);            // the resource's own handler ran
            Assert.Empty(monitor.Log);                       // the monitor was never consulted
        }

        [Fact]
        public void MultipleMonitorsHandleExactlyOnce()
        {
            // iteration order over the Monitors set is nondeterministic (HashSet) —
            // only reachability and first-match-stops are pinned
            var resource = new TestResource();
            var one = new CatchAllMonitor();
            var two = new CatchAllMonitor();
            one.StartTracking(resource);
            two.StartTracking(resource);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("bubbled"));
            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(1, one.Caught.Count + two.Caught.Count);
        }

        [Fact]
        public void BubblingIsRecursiveThroughMonitorChains()
        {
            // resource → mid monitor (no handler) → grand monitor (handler): the walk
            // recurses because each monitor is itself a Resource (Resource.cs:93-95)
            var resource = new TestResource();
            var mid = new ResourceMonitor<IInspectable>();
            var grand = new CatchAllMonitor();
            mid.StartTracking(resource);
            grand.StartTracking(mid);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("bubbled"));
            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(["bubbled"], grand.Caught);
        }

        [Fact]
        public void StopTrackingSeversTheBubblingPath()
        {
            var resource = new TestResource();
            var monitor = new CatchAllMonitor();
            monitor.StartTracking(resource);
            monitor.StopTracking(resource);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("bubbled"));
            resource.Tokens = [new TestToken()];

            // nothing handles the action → NotImplementedException through the default
            // error chain, wrapped by the sync trigger's .Wait() (Resource.cs:44)
            var aggregate = Assert.Throws<AggregateException>(() => resource.Inspect(TimeSpan.Zero));
            Assert.IsType<NotImplementedException>(aggregate.InnerException);
            Assert.Empty(monitor.Caught);
        }

        [Fact]
        public void ErrorsBubbleToTrackingMonitorSynchronously()
        {
            var resource = new TestResource();
            var monitor = new CatchAllMonitor();
            monitor.StartTracking(resource);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("no-such-action"));
            resource.Tokens = [new TestToken()];
            resource.Inspect(TimeSpan.Zero);                 // NotImplementedException → monitor swallows

            Assert.Equal(["error:NotImplementedException"], monitor.Caught);
        }

        [Fact]
        public void AdoptFalseDoesNotAdoptTheBubblingPath()
        {
            var resource = new TestResource();
            var monitor = new CatchAllMonitor();
            monitor.StartTracking(resource, adopt: false);

            ((IEventSystem)resource)["Demand"].AddAction(Actions.Named("bubbled"));   // monitor-only action
            resource.Tokens = [new TestToken()];

            // non-adoption observed: the action cannot bubble and fails instead
            var aggregate = Assert.Throws<AggregateException>(() => resource.Inspect(TimeSpan.Zero));
            Assert.IsType<NotImplementedException>(aggregate.InnerException);
            Assert.Empty(monitor.Caught);
        }

        [Fact]
        public void AdoptFalseStillTracksForInspection()
        {
            var resource = new TestResource { Tokens = [new TestToken()] };
            var monitor = new CatchAllMonitor();
            monitor.StartTracking(resource, adopt: false);

            var tokens = monitor.Inspect(TimeSpan.Zero);     // tracking observed: tokens flow up

            Assert.Single(tokens);
        }
    }
}
