using Autofac;
using MadWizard.Desomnia.Tests.Parity;
using Xunit;
using MadWizard.Desomnia.Events;

#pragma warning disable CS0067

namespace MadWizard.Desomnia.Tests.Engine
{
    /// <summary>Acceptance tests for the NEW phase-1 surface (EVENT-SYSTEM-REDESIGN.md
    /// §4/§7.1): delegate extensions, filter events, dynamic declaration with cancel
    /// relations, annotations, the Parallel option, and container attachment.</summary>
    public class EngineTests
    {
        private class Widget : EventMetaObject
        {
            public readonly List<string> Log = [];

            public event EventInvocation? Ping;

            [EventOpposite(nameof(Down))]
            public event EventInvocation? Up;
            public event EventInvocation? Down;

            [EventOptions(Parallel = true)]
            public event EventInvocation? Both;

            public event EventFilter<bool>? Allow;
            public event EventFilter<int, RichEvent>? Scale;

            [ActionHandler("mark")]
            private void Mark() => Log.Add("mark");

            // language-level entry points from inside the declaring class:
            public void FirePing() => Ping.TriggerEvent();
            public Task FireBothAsync() => Both.TriggerEventAsync();
            public void WireMark() => Ping.AddAction(new JSEventAction("mark"));
            public EventType PingMeta => Ping.Meta;
            public bool AskAllow(bool value) => Allow.Filter(value);
            public int AskScale(int value, RichEvent context) => Scale.Filter(value, context);
        }

        public class RichEvent() : Event("rich")
        {
            public int Factor { get; init; } = 1;
        }

        [Fact]
        public void DelegateExtensionsResolveTheMetaObjectViaTheAnchor()
        {
            var widget = new Widget();

            Assert.Equal("Ping", widget.PingMeta.Name);
            Assert.Same(widget, widget.PingMeta.Owner);
        }

        [Fact]
        public void TriggerAndAddActionWorkThroughTheDelegateExtensions()
        {
            var widget = new Widget();

            widget.WireMark();
            widget.FirePing();

            Assert.Equal(["mark"], widget.Log);
            Assert.Equal(1, widget.PingMeta.TriggerCount);    // the anchor observes every trigger
        }

        [Fact]
        public void UnattachedDelegateThrowsDescriptively()
        {
            EventInvocation plain = _ => Task.CompletedTask;

            Assert.Throws<InvalidOperationException>(() => plain.Meta);
        }

        [Fact]
        public async Task EventOppositeAnnotationCancelsSymmetrically()
        {
            // declared on Up only — the pair is mutual by default
            var widget = new Widget();
            ((IEventSystem)widget)["Down"].AddAction(Actions.Delayed("mark", 400));
            ((IEventSystem)widget)["Up"].AddAction(Actions.Delayed("mark", 400));

            await ((IEventSystem)widget)["Down"].TriggerEventAsync();   // arms Down's action
            await ((IEventSystem)widget)["Up"].TriggerEventAsync();     // Up cancels Down's pending...
            await ((IEventSystem)widget)["Down"].TriggerEventAsync();   // ...and Down cancels Up's

            await Wait.SettleAfter(400);
            Assert.Single(widget.Log);                       // only Down's re-armed action fired
        }

        [Fact]
        public async Task DynamicEventsDeclareAndCancelLikeClrEvents()
        {
            // the lid-plugin shape (§8.1): declare, wire config actions, trigger
            var widget = new Widget();
            var events = (IEventSystem)widget;

            var open = events.AddDynamicEvent("LidOpen", new() { Opposites = ["LidClose"] });
            var close = events.AddDynamicEvent("LidClose", new());

            close.AddAction(new JSEventAction("mark") { Delay = TimeSpan.FromMilliseconds(400) });

            close.TriggerEvent();                                 // arms
            open.TriggerEvent();                                  // symmetric relation cancels the pending

            await Wait.SettleAfter(400);
            Assert.Empty(widget.Log);

            close.TriggerEvent();                                 // undisturbed, fires normally
            await Wait.Until(() => widget.Log.Count == 1);
        }

        [Fact]
        public void DynamicDeclarationIsGuarded()
        {
            var widget = new Widget();
            var events = (IEventSystem)widget;

            events.AddDynamicEvent("Custom", new());

            Assert.Throws<ArgumentException>(() => events.AddDynamicEvent("Custom", new()));   // duplicate
            Assert.Throws<ArgumentException>(() => events.AddDynamicEvent("Ping", new()));     // collides with CLR event
            Assert.Throws<KeyNotFoundException>(() => events["Nope"]);
            Assert.True(events.HasEvent("Custom"));
        }

        [Fact]
        public async Task ParallelEventsAwaitAllEntriesConcurrently()
        {
            var widget = new Widget();
            var first = new TaskCompletionSource();
            var second = new TaskCompletionSource();

            // cross-waiting handlers: sequential await would deadlock
            widget.Both += async _ => { first.SetResult(); await second.Task; };
            widget.Both += async _ => { second.SetResult(); await first.Task; };

            await widget.FireBothAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task ProgrammaticZeroTimesFiresImmediately()
        {
            // Times = 0 must behave like the "+0x" converter default (immediate), not
            // underflow the counter into a permanently armed slot
            var widget = new Widget();
            widget.PingMeta.AddAction(new JSEventAction("mark") { Times = 0 });

            await widget.PingMeta.TriggerEventAsync();

            Assert.Equal(["mark"], widget.Log);
        }

        [Fact]
        public void FilterEventsFoldInSubscriptionOrderWithIdentityDefault()
        {
            var widget = new Widget();

            Assert.True(widget.AskAllow(true));              // no subscribers → identity

            widget.Allow += (value, _) => !value;
            widget.Allow += (value, _) => value;             // observes the previous result

            Assert.False(widget.AskAllow(true));

            widget.Scale += (value, context) => value * context.Factor;

            Assert.Equal(15, widget.AskScale(5, new RichEvent { Factor = 3 }));
        }

        [Fact]
        public void FilterEventsAreRegisteredButHaveNoActionSurface()
        {
            var widget = new Widget();
            var events = (IEventSystem)widget;

            Assert.True(events.HasEvent("Allow"));
            Assert.Contains(events.Events, e => e is FilterEventType { Name: "Allow" });
            Assert.Throws<InvalidOperationException>(() => events["Allow"]);
        }

        [Fact]
        public void MiddlewareAttachesContainerCreatedInstances()
        {
            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterType<Widget>().AsSelf();

            using var container = builder.Build();

            Assert.True(EventSystem.IsAttached(container.Resolve<Widget>()));
        }

        private class ChildWidget : EventMetaObject
        {
            public event EventInvocation? Beep;
        }

        [Fact]
        public void MiddlewareAttachesChildScopeInlineRegistrations()
        {
            // the §7.1 acceptance test: inline registrations in BeginLifetimeScope
            // lambdas (the dominant pattern in NetworkMonitor/SessionMonitor) must
            // pass through the middleware too
            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());

            using var container = builder.Build();
            using var scope = container.BeginLifetimeScope(b => b.RegisterType<ChildWidget>().AsSelf());

            Assert.True(EventSystem.IsAttached(scope.Resolve<ChildWidget>()));
        }

        private interface IWidgetService { }

        private class ServiceWidget : EventMetaObject, IWidgetService
        {
            public event EventInvocation? Beep;
        }

        private class DecoratorWidget(IWidgetService inner) : EventMetaObject, IWidgetService
        {
            public IWidgetService Inner => inner;
        }

        [Fact]
        public void MiddlewareAttachesTheInnerInstanceOfDecoratedServices()
        {
            // documented semantics: the service middleware observes the PRE-decoration
            // instance — the eventable inner object is attached, the decorator is not
            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterType<ServiceWidget>().As<IWidgetService>();
            builder.RegisterDecorator<DecoratorWidget, IWidgetService>();

            using var container = builder.Build();
            var resolved = Assert.IsType<DecoratorWidget>(container.Resolve<IWidgetService>());

            Assert.True(EventSystem.IsAttached((ServiceWidget)resolved.Inner));
            Assert.False(EventSystem.IsAttached(resolved));
        }

        [Fact]
        public void ManualConstructionIsUnattachedUntilExplicitAttach()
        {
            var widget = new Widget();

            Assert.False(EventSystem.IsAttached(widget));

            EventSystem.Attach(widget);                      // the test-harness entry

            Assert.True(EventSystem.IsAttached(widget));
        }

        private class RootProvider(List<string> log) : ActionProvider
        {
            [ActionHandler("root-only")]
            private void Handle() => log.Add("root");
        }

        [Fact]
        public async Task AttachedInstancesFallBackToTheRootWithoutAnyTreeMembership()
        {
            // the §6.3 engine guarantee that closes the newborn-monitor gap: a node with
            // no parents (never tracked by anything) resolves straight at the root
            var log = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new RootProvider(log)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<Widget>().AsSelf();

            using var container = builder.Build();
            var widget = container.Resolve<Widget>();

            widget.PingMeta.AddAction(new JSEventAction("root-only"));
            await widget.PingMeta.TriggerEventAsync();

            Assert.Equal(["root"], log);
        }

        private class HookedResource : Resource
        {
            public readonly List<string> Hooks = [];

            protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval) => [];

            protected override void OnAttachedTo(EventMetaObject parent) => Hooks.Add($"attached:{parent.GetType().Name}");

            protected override void OnDetachedFrom(EventMetaObject parent) => Hooks.Add($"detached:{parent.GetType().Name}");
        }

        private class PlainMonitor : ResourceMonitor<IInspectable> { }

        [Fact]
        public void TreeEdgesRaiseTheAttachmentHooks()
        {
            // the OnAttachedTo/OnDetachedFrom seam replaces the StartTrackingBy/
            // StopTrackingBy overrides (SessionProcessWatch's relay migrates to it)
            var resource = new HookedResource();
            var monitor = new PlainMonitor();

            monitor.StartTracking(resource);
            monitor.StopTracking(resource);
            monitor.StartTracking(resource, adopt: false);   // roster only — no edge, no hook

            Assert.Equal(["attached:PlainMonitor", "detached:PlainMonitor"], resource.Hooks);
        }

        private class TaggedMonitor(string tag, List<string> log) : ResourceMonitor<IInspectable>
        {
            [ActionHandler("claim")]
            private void Claim() => log.Add(tag);
        }

        private class PlainResource : Resource
        {
            public UsageToken[] Tokens = [];

            protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval) => Tokens;
        }

        [Fact]
        public async Task UnattachedChildOfAnAttachedMonitorKeepsRootReachability()
        {
            // the DuoInstance shape: a manually constructed resource tracked by a
            // container-created monitor — the root is reachable through ANY visited
            // node of the walk, exactly like the legacy bubbling chains
            var log = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new RootProvider(log)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<PlainMonitor>().AsSelf();

            using var container = builder.Build();
            var monitor = container.Resolve<PlainMonitor>();  // attached

            var resource = new PlainResource { Tokens = [new TestToken()] };   // manual, unattached
            monitor.StartTracking(resource);

            ((IEventSystem)resource)["Usage"].AddAction(Actions.Named("root-only"));
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(["root"], log);
        }

        [Fact]
        public async Task ChildScopeInstancesReachTheRootFallback()
        {
            // the dominant production pattern (§7.1): eventable objects born in nested
            // lifetime scopes must resolve root-only actions — proven by dispatch, not
            // just by the attachment flag
            var log = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new RootProvider(log)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();

            using var container = builder.Build();
            using var scope = container.BeginLifetimeScope(b => b.RegisterType<Widget>().AsSelf());

            var widget = scope.Resolve<Widget>();
            widget.PingMeta.AddAction(new JSEventAction("root-only"));

            await widget.PingMeta.TriggerEventAsync();

            Assert.Equal(["root"], log);
        }

        [Fact]
        public void DisposedRosterMembersAreEvictedLazily()
        {
            // roster disposal backstop (§7.1): a member disposed WITHOUT an explicit
            // StopTracking (crash path) must never be inspected again
            var monitor = new PlainMonitor();
            var resource = new PlainResource { Tokens = [new TestToken()] };
            monitor.StartTracking(resource);

            resource.Dispose();

            Assert.Empty(monitor.Inspect(TimeSpan.Zero));    // evicted, not inspected
            Assert.False(monitor.StartTracking(resource));   // and never re-adopted
        }

        private class ContextRootProvider(List<object?> log) : ActionProvider
        {
            [ActionHandler("catch-context")]
            private void Handle(Version stamp) => log.Add(stamp);
        }

        private class SnapshotWidget : EventMetaObject
        {
            [EventContext]
            public Version? Stamp { get; set; }
        }

        private (Autofac.IContainer Container, List<object?> Log) BuildOrphanContainer()
        {
            var log = new List<object?>();
            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new ContextRootProvider(log)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<SnapshotWidget>().AsSelf();

            return (builder.Build(), log);
        }

        [Fact]
        public void OrphanedEventsReplayBoundActionsAtTheRootWithTheContextSnapshot()
        {
            // the lid shape (§7.4): the owner dies, the handle lives on — triggers
            // resolve at the root, carrying the [EventContext] values snapshotted at
            // the orphaning seam (NOT the dead owner's current state)
            var (container, log) = BuildOrphanContainer();
            using var _ = container;

            var widget = container.Resolve<SnapshotWidget>();
            widget.Stamp = new Version(9, 9);

            var gone = ((IEventSystem)widget).AddDynamicEvent("Gone", new());
            gone.AddAction(new JSEventAction("catch-context"));

            gone.Orphan();
            widget.Stamp = null;                              // the owner's state is dead now
            widget.Dispose();

            Assert.True(gone.IsOrphaned);
            gone.TriggerEvent();

            Assert.Equal([new Version(9, 9)], log);           // snapshot, via the root

            gone.Orphan();                                    // idempotent
        }

        [Fact]
        public async Task OrphanedPairKeepsItsOppositeCancellation()
        {
            var (container, log) = BuildOrphanContainer();
            using var _ = container;

            var widget = container.Resolve<SnapshotWidget>();
            widget.Stamp = new Version(1, 0);

            var events = (IEventSystem)widget;
            var open = events.AddDynamicEvent("LidOpen", new() { Opposites = ["LidClose"] });
            var close = events.AddDynamicEvent("LidClose", new());
            close.AddAction(new JSEventAction("catch-context") { Delay = TimeSpan.FromMilliseconds(400) });

            open.Orphan();
            close.Orphan();

            close.TriggerEvent();                                  // arms in orphan mode
            open.TriggerEvent();                                   // the pair still cancels each other

            await Wait.SettleAfter(400);
            Assert.Empty(log);

            close.TriggerEvent();                                  // undisturbed → fires at the root
            await Wait.Until(() => log.Count == 1);
        }

        [Fact]
        public async Task OrphaningCancelsLivePendingsAndDropsSubscribers()
        {
            var (container, log) = BuildOrphanContainer();
            using var _ = container;

            var widget = container.Resolve<SnapshotWidget>();
            widget.Stamp = new Version(1, 0);

            var gone = ((IEventSystem)widget).AddDynamicEvent("Gone", new());
            gone.AddAction(new JSEventAction("catch-context") { Delay = TimeSpan.FromMilliseconds(150) });

            var subscriberRan = false;
            gone.AddHandler(_ => { subscriberRan = true; return Task.CompletedTask; });

            gone.TriggerEvent();                                   // arms while alive (subscriber runs)
            gone.Orphan();                                    // §7.4: live pendings die at the seam

            await Wait.SettleAfter(150);
            Assert.Empty(log);

            subscriberRan = false;
            gone.TriggerEvent();                                   // orphan replay: bound actions only

            await Wait.Until(() => log.Count == 1);
            Assert.False(subscriberRan);
        }

        [Fact]
        public void ClrEventsCannotBeOrphaned()
        {
            var widget = new Widget();

            Assert.Throws<NotSupportedException>(() => ((IEventSystem)widget)["Ping"].Orphan());
        }

        private class UrlProvider(List<string> log) : ActionProvider
        {
            [URLActionHandler("test")]
            private void Handle(Uri url) => log.Add($"test:{url.OriginalString}");
        }

        /// <summary>A node declaring its OWN URL handler — the annotation surface,
        /// fully symmetric to [ActionHandler] (§6.4).</summary>
        private class UrlWidget : EventMetaObject
        {
            public readonly List<string> Log = [];

            public event EventInvocation? Ping;

            public EventType PingMeta => Ping.Meta;

            [URLActionHandler("test")]
            private void Handle(Uri url) => Log.Add($"test:{url.OriginalString}");
        }

        [Fact]
        public async Task UrlActionsResolveAtTheRootProviders()
        {
            var log = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new UrlProvider(log)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<Widget>().AsSelf();

            using var container = builder.Build();
            var widget = container.Resolve<Widget>();

            widget.PingMeta.AddAction(new URLEventAction(new Uri("test://box/thing?x=1")));
            await widget.PingMeta.TriggerEventAsync();

            Assert.Equal(["test:test://box/thing?x=1"], log);
        }

        [Fact]
        public async Task NodeUrlHandlersWinOverTheRoot()
        {
            var rootLog = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new UrlProvider(rootLog)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<UrlWidget>().AsSelf();

            using var container = builder.Build();
            var widget = container.Resolve<UrlWidget>();

            widget.PingMeta.AddAction(new URLEventAction(new Uri("test://nearest/wins")));
            await widget.PingMeta.TriggerEventAsync();

            Assert.Equal(["test:test://nearest/wins"], widget.Log);
            Assert.Empty(rootLog);
        }

        private class OtherSchemeResource : PlainResource
        {
            [URLActionHandler("other")]
            private void Handle(Uri url) { }                  // wrong scheme — must not stop the walk
        }

        private class UrlMonitor(List<string> log) : ResourceMonitor<IInspectable>
        {
            [URLActionHandler("test")]
            private void Handle(Uri url) => log.Add($"test:{url.OriginalString}");
        }

        [Fact]
        public async Task UrlActionsResolveAtTheNearestAncestorUrlHandler()
        {
            // §6.4 walk semantics: no matching handler on the origin → the walk continues
            // to the parents; a handler for a DIFFERENT scheme must not stop it either
            var log = new List<string>();
            var resource = new OtherSchemeResource { Tokens = [new TestToken()] };
            var monitor = new UrlMonitor(log);
            monitor.StartTracking(resource);

            ((IEventSystem)resource)["Usage"].AddAction(new URLEventAction(new Uri("test://via/parent")));
            resource.Inspect(TimeSpan.Zero);

            await Wait.Until(() => log.Count == 1);
            Assert.Equal(["test:test://via/parent"], log);
        }

        [Fact]
        public void OrphanedUrlActionsResolveAtTheRootProviders()
        {
            // §7.4 × §6.4 composition: a node's [URLActionHandler]s die with their owner —
            // orphan replay resolves URL actions against the root providers only
            var log = new List<string>();

            var builder = new ContainerBuilder();
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());
            builder.RegisterInstance(new UrlProvider(log)).As<ActionProvider>();
            builder.RegisterInstance(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionManager>.Instance)
                .As<Microsoft.Extensions.Logging.ILogger<ActionManager>>();
            builder.RegisterType<ActionManager>().AsImplementedInterfaces().SingleInstance().AsSelf();
            builder.RegisterType<SnapshotWidget>().AsSelf();

            using var container = builder.Build();
            var widget = container.Resolve<SnapshotWidget>();

            var gone = ((IEventSystem)widget).AddDynamicEvent("Gone", new());
            gone.AddAction(new URLEventAction(new Uri("test://after/death")));

            gone.Orphan();
            widget.Dispose();

            gone.TriggerEvent();

            Assert.Equal(["test:test://after/death"], log);
        }

        [Fact]
        public async Task DelayedUrlActionsRunThroughTheScheduler()
        {
            var widget = new UrlWidget();

            widget.PingMeta.AddAction(new URLEventAction(new Uri("test://later")) { Delay = TimeSpan.FromMilliseconds(100) });

            await widget.PingMeta.TriggerEventAsync();
            Assert.Empty(widget.Log);                         // armed, suffix never reaches the handler

            await Wait.Until(() => widget.Log.Count == 1);
        }

        private class UrlErrorActor : EventMetaObject
        {
            public readonly List<ActionError> Errors = [];

            public event EventInvocation? Go;

            protected override bool OnActionError(ActionError error)
            {
                Errors.Add(error);
                return true;
            }
        }

        [Fact]
        public async Task UnknownSchemesRouteThroughTheErrorChain()
        {
            var actor = new UrlErrorActor();

            ((IEventSystem)actor)["Go"].AddAction(new URLEventAction(new Uri("nope://nobody/home")));
            await ((IEventSystem)actor)["Go"].TriggerEventAsync();

            var error = Assert.Single(actor.Errors);
            Assert.IsType<NotImplementedException>(error.Exception);
        }

        [Fact]
        public void ParentsAreConsultedInInsertionOrder()
        {
            // the legacy Monitors HashSet made bubbling order nondeterministic — the
            // engine's ordered edges make first-tracked-wins a real contract
            var log = new List<string>();
            var resource = new PlainResource { Tokens = [new TestToken()] };
            var first = new TaggedMonitor("first", log);
            var second = new TaggedMonitor("second", log);

            first.StartTracking(resource);
            second.StartTracking(resource);

            ((IEventSystem)resource)["Usage"].AddAction(Actions.Named("claim"));
            resource.Inspect(TimeSpan.Zero);

            Assert.Equal(["first"], log);
        }
    }
}
