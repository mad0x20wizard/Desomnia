using Autofac;
using MadWizard.Desomnia.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MadWizard.Desomnia.Events;

namespace MadWizard.Desomnia.Tests.Parity
{
    /// <summary>§9.1: root catch-all semantics — registration-order iteration, first match
    /// wins, exception-counts-as-handled, HandleActionError always true
    /// (ActionManager.cs:36-67).</summary>
    public class ActionManagerParityTests
    {
        private class ProviderActor(string tag, List<string> log) : ActionProvider
        {
            [ActionHandler("shared")]
            private void Shared() => log.Add(tag);
        }

        private class ThrowingActor : ActionProvider
        {
            [ActionHandler("shared")]
            private void Shared() => throw new InvalidOperationException("provider failed");
        }

        private static ActionManager Create(params ActionProvider[] providers)
        {
            var manager = new ActionManager
            {
                Logger = NullLogger<ActionManager>.Instance,
                InjectableProviders = providers,
            };

            ((IStartable)manager).Start();

            return manager;
        }

        [Fact]
        public async Task FirstRegisteredActorWinsInRegistrationOrder()
        {
            var log = new List<string>();
            var manager = Create(new ProviderActor("first", log), new ProviderActor("second", log));

            var handled = await manager.TryHandleEventAction(new Event("X"), Actions.Command("shared"));

            Assert.True(handled);
            Assert.Equal(["first"], log);
        }

        [Fact]
        public async Task UnknownActionReturnsFalse()
        {
            var manager = Create(new ProviderActor("only", []));

            Assert.False(await manager.TryHandleEventAction(new Event("X"), Actions.Command("nope")));
        }

        [Fact]
        public async Task ThrowingActorCountsAsHandledAndStopsIteration()
        {
            var log = new List<string>();
            var manager = Create(new ThrowingActor(), new ProviderActor("never", log));

            var handled = await manager.TryHandleEventAction(new Event("X"), Actions.Command("shared"));

            Assert.True(handled);        // exception-counts-as-handled (ActionManager.cs:47-52)
            Assert.Empty(log);           // later actors never consulted
        }

        [Fact]
        public void HandleActionErrorAlwaysReturnsTrue()
        {
            var manager = Create();

            var error = new ActionError(new Event("X"), Actions.Command("any"), new InvalidOperationException());

            Assert.True(manager.HandleActionError(error));
        }


        private class SilentActor : ActionProvider { }

        [Fact]
        public async Task LaterActorHandlesWhenEarlierDoesNot()
        {
            // registration-order ITERATION, not just first-preference: an earlier actor
            // returning false falls through to the next (ActionManager.cs:38-45)
            var log = new List<string>();
            var manager = Create(new SilentActor(), new ProviderActor("second", log));

            var handled = await manager.TryHandleEventAction(new Event("X"), Actions.Command("shared"));

            Assert.True(handled);
            Assert.Equal(["second"], log);
        }

        [Fact]
        public async Task ContainerResolvedActorsArriveInRegistrationOrder()
        {
            // the other half of the §9.1 ordering claim: Autofac's IEnumerable<Actor>
            // preserves registration order into InjectableActors
            var log = new List<string>();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new ProviderActor("first", log)).As<ActionProvider>();
            builder.RegisterInstance(new ProviderActor("second", log)).As<ActionProvider>();
            builder.RegisterInstance(NullLogger<ActionManager>.Instance).As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().SingleInstance();

            using var container = builder.Build();
            var manager = container.Resolve<ActionManager>();
            ((IStartable)manager).Start();

            await manager.TryHandleEventAction(new Event("X"), Actions.Command("shared"));

            Assert.Equal(["first"], log);
        }

        private class ChainResource : Resource
        {
            public UsageToken[] Tokens = [];

            protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval) => Tokens;
        }

        private static Autofac.IContainer BuildChainContainer(ActionProvider rootProvider)
        {
            // the real seam: the engine's root fallback replaces the old SystemMonitor
            // forwarding overrides (SystemMonitor.cs:160-176, deleted in phase 2)
            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(rootProvider).As<ActionProvider>();
            builder.RegisterInstance(NullLogger<ActionManager>.Instance).As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<ChainResource>().AsSelf();

            return builder.Build();
        }

        [Fact]
        public void ResourceActionReachesRootProviderThroughTheFullChain()
        {
            var log = new List<string>();

            using var container = BuildChainContainer(new ProviderActor("root", log));
            var resource = container.Resolve<ChainResource>();
            resource.Tokens = [new TestToken()];

            ((IEventSystem)resource)["Usage"].AddAction(Actions.Named("shared"));   // only the root has it
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(["root"], log);
        }

        [Fact]
        public void ThrowingRootProviderIsSwallowedEndToEnd()
        {
            // exception-counts-as-handled composes through the whole chain: the trigger
            // caller observes nothing
            using var container = BuildChainContainer(new ThrowingActor());
            var resource = container.Resolve<ChainResource>();
            resource.Tokens = [new TestToken()];

            ((IEventSystem)resource)["Usage"].AddAction(Actions.Named("shared"));
            resource.Inspect(TimeSpan.Zero);                 // completes without throwing
        }
    }
}
