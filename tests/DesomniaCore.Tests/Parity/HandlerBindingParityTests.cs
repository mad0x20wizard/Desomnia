using System.Reflection;
using Xunit;
using MadWizard.Desomnia.Events;

namespace MadWizard.Desomnia.Tests.Parity
{
    /// <summary>§9.1: parameter binding order (context → positional argument → default →
    /// skip), the non-concurrent invocation guard, and error-chain semantics including
    /// TargetInvocationException unwrapping.</summary>
    public class HandlerBindingParityTests
    {
        private class BindingActor : TestEvents
        {
            public readonly List<object?> Received = [];

            [ActionHandler("wants-event")]
            private void WantsEvent(Event e) => Received.Add(e);

            [ActionHandler("wants-source")]
            private void WantsSource(BindingActor source) => Received.Add(source);

            [ActionHandler("wants-string")]
            private void WantsString(string value) => Received.Add(value);

            [ActionHandler("wants-default")]
            private void WantsDefault(string value = "fallback") => Received.Add(value);

            [ActionHandler("unsatisfiable")]
            private void Unsatisfiable(Uri required) => Received.Add(required);

            [ActionHandler("multi")]
            private void Multi(Event e, string a, string b)
            {
                Received.Add(e);
                Received.Add(a);
                Received.Add(b);
            }
        }

        [Fact]
        public async Task EventParameterBindsFromContext()
        {
            var actor = new BindingActor();
            var @event = new Event("X");

            Assert.True(await actor.TryHandleEventAction(@event, Actions.Command("wants-event")));
            Assert.Same(@event, actor.Received.Single());
        }

        [Fact]
        public async Task SourceParameterBindsFromContext()
        {
            var actor = new BindingActor();
            actor.AddEventAction("Alpha", Actions.Named("wants-source"));

            await actor.DoTriggerAsync("Alpha");     // Source is stamped into the context

            Assert.Same(actor, actor.Received.Single());
        }

        [Fact]
        public async Task ContextObjectWinsOverPositionalArgument()
        {
            var actor = new BindingActor();
            var @event = new Event("X");
            @event.AddContext("from-context");

            await actor.TryHandleEventAction(@event, Actions.Command("wants-string", "from-args"));

            Assert.Equal("from-context", actor.Received.Single());
        }

        [Fact]
        public async Task PositionalArgumentBindsWhenNoContextMatches()
        {
            var actor = new BindingActor();

            await actor.TryHandleEventAction(new Event("X"), Actions.Command("wants-string", "from-args"));

            Assert.Equal("from-args", actor.Received.Single());
        }

        [Fact]
        public async Task ParameterDefaultAppliesWhenNothingBinds()
        {
            var actor = new BindingActor();

            await actor.TryHandleEventAction(new Event("X"), Actions.Command("wants-default"));

            Assert.Equal("fallback", actor.Received.Single());
        }

        [Fact]
        public async Task UnsatisfiableParameterSkipsInvocationButCountsAsHandled()
        {
            var actor = new BindingActor();

            var handled = await actor.TryHandleEventAction(new Event("X"), Actions.Command("unsatisfiable"));

            Assert.True(handled);            // handler exists → "handled", even though skipped
            Assert.Empty(actor.Received);    // but never invoked
        }

        [Fact]
        public async Task ContextBoundParametersDoNotConsumePositionalArguments()
        {
            // argsIndex only advances when an argument is consumed (ActionHandler.cs:35-38):
            // the Event binds from context, then "x" and "y" map to the string slots in order
            var actor = new BindingActor();
            var @event = new Event("X");

            await actor.TryHandleEventAction(@event, Actions.Command("multi", "x", "y"));

            Assert.Equal([@event, "x", "y"], actor.Received);
        }

        [Fact]
        public async Task ContextObjectIsConsumedOnceBound()
        {
            // flipped quirk (phase 1, §9.3): a context object satisfies at most ONE
            // parameter — the second string slot falls back to the positional argument
            var actor = new BindingActor();
            var @event = new Event("X");
            @event.AddContext("ctx");

            await actor.TryHandleEventAction(@event, Actions.Command("multi", "from-args"));

            Assert.Equal([@event, "ctx", "from-args"], actor.Received);
        }

        [Fact]
        public async Task UnknownActionNameIsNotHandled()
        {
            var actor = new BindingActor();

            Assert.False(await actor.TryHandleEventAction(new Event("X"), Actions.Command("no-such-action")));
        }

        private class ConcurrencyActor : TestEvents
        {
            public readonly SemaphoreSlim Entered = new(0);
            public readonly TaskCompletionSource Gate = new();

            [ActionHandler("serial")]
            private async Task Serial() { Entered.Release(); await Gate.Task; }

            [ActionHandler("parallel", Concurrent = true)]
            private async Task Parallel() { Entered.Release(); await Gate.Task; }
        }

        [Fact]
        public async Task NonConcurrentHandlerSkipsOverlappingInvocationSilently()
        {
            var actor = new ConcurrencyActor();
            actor.AddEventAction("Alpha", Actions.Named("serial"));

            var running = actor.DoTriggerAsync("Alpha");
            Assert.True(await actor.Entered.WaitAsync(TimeSpan.FromSeconds(5)));

            await actor.DoTriggerAsync("Alpha");     // overlaps → silently skipped, still "handled"

            Assert.Equal(0, actor.Entered.CurrentCount);

            actor.Gate.SetResult();
            await running;
        }

        [Fact]
        public async Task ConcurrentHandlerAllowsOverlappingInvocations()
        {
            var actor = new ConcurrencyActor();
            actor.AddEventAction("Alpha", Actions.Named("parallel"));

            var first = actor.DoTriggerAsync("Alpha");
            await actor.Entered.WaitAsync(TimeSpan.FromSeconds(5));

            var second = actor.DoTriggerAsync("Alpha");
            var entered = await actor.Entered.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(entered);                    // second invocation entered while first pending

            actor.Gate.SetResult();
            await Task.WhenAll(first, second);
        }

        private class DetachedActor : TestEvents
        {
            public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            [ActionHandler("detached", Detached = true)]
            private void Detached()
            {
                Entered.TrySetResult();
                Release.Task.GetAwaiter().GetResult();
                Completed.TrySetResult();
            }
        }

        [Fact]
        public async Task DetachedHandlerDoesNotDelayTriggerCompletion()
        {
            var actor = new DetachedActor();
            actor.AddEventAction("Alpha", Actions.Named("detached"));

            var trigger = actor.DoTriggerAsync("Alpha");

            try
            {
                await trigger.WaitAsync(TimeSpan.FromSeconds(5));
                await actor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(actor.Completed.Task.IsCompleted);
            }
            finally
            {
                actor.Release.TrySetResult();
            }

            await actor.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private class ErrorActor : TestEvents
        {
            public readonly List<ActionError> Errors = [];
            public bool Swallow = true;

            [ActionHandler("boom-void")]
            private void BoomVoid() => throw new InvalidOperationException("boom");

            [ActionHandler("boom-task")]
            private async Task BoomTask() { await Task.Yield(); throw new InvalidOperationException("boom-async"); }

            [ActionHandler("boom-task-sync")]
            private Task BoomTaskSync() => throw new InvalidOperationException("boom-sync");

            [ActionHandler("boom-task-detached", Detached = true)]
            private async Task BoomTaskDetached() { await Task.Yield(); throw new InvalidOperationException("boom-detached"); }

            protected override bool OnActionError(ActionError error)
            {
                Errors.Add(error);
                return Swallow;
            }
        }

        [Fact]
        public async Task VoidHandlerExceptionIsUnwrappedFromTargetInvocationException()
        {
            var actor = new ErrorActor();
            actor.AddEventAction("Alpha", Actions.Named("boom-void"));

            await actor.DoTriggerAsync("Alpha");     // swallowed by the override

            var error = Assert.Single(actor.Errors);
            Assert.IsType<InvalidOperationException>(error.Exception);   // TIE unwrapped
            Assert.Same(actor, error.Actor);                             // inner error names the actor
        }

        [Fact]
        public async Task TaskHandlerExceptionPassesThroughUnwrapped()
        {
            var actor = new ErrorActor();
            actor.AddEventAction("Alpha", Actions.Named("boom-task"));

            await actor.DoTriggerAsync("Alpha");

            var error = Assert.Single(actor.Errors);
            Assert.IsType<InvalidOperationException>(error.Exception);
            Assert.Equal("boom-async", error.Exception!.Message);
        }

        [Fact]
        public async Task SyncTaskHandlerExceptionIsUnwrappedInTheErrorObject()
        {
            // a non-async Task-returning handler throwing before producing a task goes
            // through Method.Invoke → TargetInvocationException, like void handlers
            var actor = new ErrorActor();
            actor.AddEventAction("Alpha", Actions.Named("boom-task-sync"));

            await actor.DoTriggerAsync("Alpha");

            var error = Assert.Single(actor.Errors);
            Assert.IsType<InvalidOperationException>(error.Exception);
            Assert.Equal("boom-sync", error.Exception!.Message);
        }

        [Fact]
        public async Task UnswallowedErrorPropagatesToTheTriggerCaller()
        {
            // true today AND after the redesign's error streamlining (§9.3): a Task
            // handler's unswallowed exception reaches the caller unwrapped; the
            // double-surface shape and the void-handler TIE wrapping are quirks
            var actor = new ErrorActor { Swallow = false };
            actor.AddEventAction("Alpha", Actions.Named("boom-task"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => actor.DoTriggerAsync("Alpha"));

            Assert.NotEmpty(actor.Errors);
            Assert.All(actor.Errors, e => Assert.IsType<InvalidOperationException>(e.Exception));
        }

        [Fact]
        public async Task UnswallowedVoidHandlerErrorSurfacesOnceUnwrapped()
        {
            // flipped quirk (phase 1, §9.3): single surface (with Actor), and the
            // trigger caller sees the UNWRAPPED exception — never a TargetInvocationException
            var actor = new ErrorActor { Swallow = false };
            actor.AddEventAction("Alpha", Actions.Named("boom-void"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => actor.DoTriggerAsync("Alpha"));

            var error = Assert.Single(actor.Errors);
            Assert.Same(actor, error.Actor);
            Assert.IsType<InvalidOperationException>(error.Exception);
        }

        [Fact]
        public async Task DelayedUnswallowedErrorIsRoutedOnceAndNeverCrashes()
        {
            // flipped quirk (phase 1, §9.2): the scheduled path routes the error through
            // HandleActionError exactly once; an unswallowed error is logged, not lost
            var actor = new ErrorActor { Swallow = false };
            actor.AddEventAction("Alpha", Actions.Delayed("boom-task", 50));

            await actor.DoTriggerAsync("Alpha");             // completes normally (only arms)

            await Wait.Until(() => actor.Errors.Count == 1);
            Assert.Same(actor, actor.Errors[0].Actor);
            await Wait.Settle();                             // no escape, no crash, no second surface
            Assert.Single(actor.Errors);
        }

        [Fact]
        public async Task DetachedUnswallowedErrorIsRoutedOnceAndNeverEscapesTheTrigger()
        {
            var actor = new ErrorActor { Swallow = false };
            actor.AddEventAction("Alpha", Actions.Named("boom-task-detached"));

            await actor.DoTriggerAsync("Alpha");

            await Wait.Until(() => actor.Errors.Count == 1);
            Assert.Same(actor, actor.Errors[0].Actor);
            Assert.IsType<InvalidOperationException>(actor.Errors[0].Exception);
            await Wait.Settle();
            Assert.Single(actor.Errors);
        }

        [Fact]
        public async Task DelayedActionInfoErrorRoutesThroughTheErrorChain()
        {
            // handler failures on the scheduled path still reach HandleActionError,
            // just later, from inside the fire task (Actor.cs:28-44 wrapped at :60)
            var actor = new ErrorActor();
            actor.AddEventAction("Alpha", Actions.Delayed("boom-task", 50));

            await actor.DoTriggerAsync("Alpha");

            await Wait.Until(() => actor.Errors.Count == 1);
            Assert.IsType<InvalidOperationException>(actor.Errors[0].Exception);
            Assert.Same(actor, actor.Errors[0].Actor);
        }

        [Fact]
        public async Task DelayedMissingActionRoutesThroughTheErrorChain()
        {
            var actor = new ErrorActor();
            actor.AddEventAction("Alpha", Actions.Delayed("no-such-action", 50));

            await actor.DoTriggerAsync("Alpha");

            await Wait.Until(() => actor.Errors.Count == 1);
            Assert.IsType<NotImplementedException>(actor.Errors[0].Exception);
        }

        [Fact]
        public async Task MissingActionOnTriggerRaisesNotImplementedThroughErrorChain()
        {
            var actor = new ErrorActor();
            actor.AddEventAction("Alpha", Actions.Named("no-such-action"));

            await actor.DoTriggerAsync("Alpha");     // swallowed

            var error = Assert.Single(actor.Errors);
            Assert.IsType<NotImplementedException>(error.Exception);
        }
    }
}
