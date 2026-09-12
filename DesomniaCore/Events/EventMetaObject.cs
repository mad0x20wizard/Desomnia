using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace MadWizard.Desomnia.Events
{
    /// <summary>
    /// The external surface of the event system on any event-able object. Implemented
    /// EXPLICITLY by <see cref="EventMetaObject"/> so it never pollutes the domain API —
    /// cast to manipulate events from outside the declaring class:
    /// <code>((IEventSystem)monitor)["LidOpen"].Trigger();</code>
    /// </summary>
    public interface IEventSystem
    {
        bool HasEvent(string name);

        IEnumerable<EventTypeBase> Events { get; }

        /// <summary>Throws KeyNotFoundException for unknown names and
        /// InvalidOperationException for filter events (no action surface).</summary>
        EventType this[string name] { get; }

        /// <summary>Registers a new named event at runtime. Behaves exactly like a
        /// CLR-declared event towards AddAction/AddHandler/Trigger. Throws
        /// ArgumentException on duplicates, InvalidOperationException if the type's
        /// policy forbids dynamic declaration.</summary>
        EventType AddDynamicEvent(string name, EventDescriptor descriptor);
    }

    /// <summary>
    /// Base class of every event-able object: builds the per-instance event registry
    /// from the CLR declaration surface (cached per type), seeds the sentinel anchors,
    /// and hosts the trigger pipeline. The base constructor alone makes the instance
    /// locally self-sufficient — constructor-time wiring and triggering are fully
    /// supported (§2 of the redesign spec).
    /// </summary>
    public abstract class EventMetaObject : IEventSystem, IDisposable
    {
        private static readonly ConcurrentDictionary<Type, TypeMetadata> _metadata = new();

        private static readonly AsyncLocal<bool> _inPipeline = new();

        internal static bool InPipeline => _inPipeline.Value;

        /// <summary>Detached continuations (delayed/throttled fires) inherit the pipeline's
        /// ExecutionContext — they clear the flag so bypass diagnostics stay meaningful.</summary>
        internal static void ExitPipelineFlow() => _inPipeline.Value = false;

        internal volatile bool IsEngineDisposed;

        /// <summary>True once <see cref="Dispose"/> ran — pendings are cancelled and
        /// arming is refused from then on. Holders of event handles can use this to
        /// detect a skipped shutdown seam (and orphan defensively).</summary>
        public bool IsDisposed => IsEngineDisposed;

        private readonly Dictionary<string, EventTypeBase> _events = [];
        private readonly Lock _eventsLock = new();

        private readonly List<EventMetaObject> _parents = [];
        private readonly Lock _parentsLock = new();

        /// <summary>Root fallback access, wired at attachment (§6.3): action reachability
        /// is independent of tree membership. Null on unattached (manual) instances.</summary>
        internal Func<IEventSystemRoot?>? RootAccessor;

        private readonly Dictionary<string, ActionHandler> _actionHandlers;
        private readonly Dictionary<string, ActionHandler> _urlActionHandlers;

        private readonly TypeMetadata _meta;

        private ILogger? _logger;
        private bool _loggerResolved;

        protected EventMetaObject()
        {
            _meta = _metadata.GetOrAdd(GetType(), BuildMetadata);

            foreach (var record in _meta.Events)
            {
                var type = new CLREventType(this, record.Info.Name, record.Descriptor, record.Info, record.Field);

                type.SeedAnchor();

                _events.Add(record.Info.Name, type);
            }

            foreach (var record in _meta.Filters)
            {
                _events.Add(record.Info.Name, new FilterEventType(this, record.Info.Name, record.Descriptor, record.Field));
            }

            RecomputeCancelRelations();

            _actionHandlers = _meta.Handlers.ToDictionary(pair => pair.Key, pair => new ActionHandler(pair.Value));
            _urlActionHandlers = _meta.URLHandlers.ToDictionary(pair => pair.Key, pair => new ActionHandler(pair.Value), StringComparer.OrdinalIgnoreCase);
        }

        #region Protected virtual hooks — in-place augmentation for the declaring class

        /// <summary>Veto seam: return false to stop the trigger entirely (nothing else
        /// happens — no cancel enforcement, no handlers, no actions).</summary>
        protected virtual bool ShouldTriggerEvent(Event @event) => true;

        protected virtual void OnEventTriggered(Event @event) { }

        /// <summary>Error seam: return true to swallow the error. Consulted exactly once
        /// per error (§9.3 — the double-surface of the old implementation is gone).</summary>
        protected virtual bool OnActionError(ActionError error) => false;

        /// <summary>Policy seam: forbid dynamic event declaration per type.</summary>
        protected virtual bool AllowEventDeclaration(EventDescriptor descriptor) => true;

        /// <summary>Tree edge notifications (wired in phase 2).</summary>
        protected virtual void OnAttachedTo(EventMetaObject parent) { }
        protected virtual void OnDetachedFrom(EventMetaObject parent) { }

        #endregion

        #region IEventSystem (explicit)

        EventType IEventSystem.this[string name] => GetEventType(name);

        EventType IEventSystem.AddDynamicEvent(string name, EventDescriptor descriptor) => AddDynamicEventCore(name, descriptor);

        bool IEventSystem.HasEvent(string name)
        {
            lock (_eventsLock)
                return _events.ContainsKey(name);
        }

        IEnumerable<EventTypeBase> IEventSystem.Events
        {
            get { lock (_eventsLock) return [.. _events.Values]; }
        }

        private EventType AddDynamicEventCore(string name, EventDescriptor descriptor)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(descriptor);

            if (!AllowEventDeclaration(descriptor))
                throw new InvalidOperationException($"{GetType().Name} does not allow dynamic event declaration ('{name}')");

            lock (_eventsLock)
            {
                if (_events.ContainsKey(name))
                    throw new ArgumentException($"{GetType().Name} already declares an event named '{name}'", nameof(name));

                var type = new DynamicEventType(this, name, descriptor);

                type.SeedAnchor();

                _events.Add(name, type);

                RecomputeCancelRelations();

                return type;
            }
        }

        #endregion

        #region Registry access

        internal EventType GetEventType(string name)
        {
            lock (_eventsLock)
            {
                if (!_events.TryGetValue(name, out var type))
                    throw new KeyNotFoundException($"{GetType().Name} has no event '{name}'");

                if (type is not EventType eventType)
                    throw new InvalidOperationException($"'{name}' is a filter event on {GetType().Name} — filters have no trigger/action surface");

                return eventType;
            }
        }

        internal EventType? FindEventType(string name)
        {
            lock (_eventsLock)
                return _events.TryGetValue(name, out var type) ? type as EventType : null;
        }

        /// <summary>The [EventContext] values stamped into every triggered event —
        /// overridden by the orphan host to serve the snapshot of its dead owner.</summary>
        private protected virtual IEnumerable<object> HarvestContext()
        {
            foreach (var property in _meta.ContextProperties)
            {
                if (property.GetValue(this) is object context)
                    yield return context;
            }
        }

        #endregion

        #region Orphaning (§7.4)

        private OrphanedEventHost? _orphanHost;

        internal void OrphanEventType(EventTypeBase type)
        {
            if (type.IsOrphaned)
                return;                                       // truly idempotent — a second Orphan must
                                                              // never re-orphan into a host-of-host or
            if (type is not DynamicEventType declared)       // kill pendings armed in orphan mode
                throw new NotSupportedException(
                    $"{GetType().Name}.{type.Name}: only runtime-declared events can be orphaned — CLR events live in their declaring object");

            // snapshot OUTSIDE the engine lock (user getters must never run under it) and
            // resilient per property: orphaning may happen defensively on an already-dying
            // owner whose getters can throw
            var snapshot = SnapshotContext();

            OrphanedEventHost host;

            lock (_eventsLock)
            {
                if (!_events.Remove(type.Name))
                    return;

                host = _orphanHost ??= new OrphanedEventHost(snapshot, RootAccessor);

                RecomputeCancelRelations();
            }

            declared.CancelActions();                                // pendings armed while alive die here (§7.4)
            declared.StripSubscribers();                      // subscribers at orphan time are dropped
            declared.ReOwn(host);

            host.AdoptOrphan(declared);                       // co-orphaned events keep their cancel
        }                                                     // relations among each other (LidOpen/LidClose)

        private object[] SnapshotContext()
        {
            List<object> snapshot = [];

            foreach (var property in _meta.ContextProperties)
            {
                try
                {
                    if (property.GetValue(this) is object context)
                        snapshot.Add(context);
                }
                catch
                {
                    // a dying owner's getter may throw — skip, keep the rest
                }
            }

            return [.. snapshot];
        }

        /// <summary>The "detached null-object" of §4.2: a minimal stand-in owner for
        /// orphaned events — no handlers, no parents, no events of its own; it serves
        /// the context snapshot and routes bound actions to the root.</summary>
        private sealed class OrphanedEventHost : EventMetaObject
        {
            private readonly object[] _snapshot;

            internal OrphanedEventHost(object[] snapshot, Func<IEventSystemRoot?>? root)
            {
                _snapshot = snapshot;
                RootAccessor = root;
            }

            private protected override IEnumerable<object> HarvestContext() => _snapshot;

            internal void AdoptOrphan(DynamicEventType type)
            {
                lock (_eventsLock)
                {
                    _events.Add(type.Name, type);

                    RecomputeCancelRelations();
                }
            }
        }

        private void RecomputeCancelRelations()
        {
            // under _eventsLock (or ctor): merge Opposites (both directions) + Cancels.
            // Each type's set is REPLACED atomically, never mutated in place — an
            // in-flight trigger on another lock reads a coherent snapshot.
            Dictionary<EventTypeBase, HashSet<string>> merged = [];

            foreach (var type in _events.Values)
                merged[type] = [.. type.Descriptor.Opposites, .. type.Descriptor.Cancels];

            foreach (var type in _events.Values)
                foreach (var opposite in type.Descriptor.Opposites)
                    if (_events.TryGetValue(opposite, out var other))
                        merged[other].Add(type.Name);

            foreach (var (type, cancels) in merged)
                type.EffectiveCancels = [.. cancels];
        }

        #endregion

        #region Trigger pipeline

        internal Task RouteTriggerAsync(Event @event) => EngineTriggerAsync(@event);   // veto = OnEventTriggering

        internal async Task EngineTriggerAsync(Event @event)
        {
            if (FindEventType(@event.Type) is not EventType type)
            {
                // legal race guard: a trigger crossing the orphan hand-over window
                // belongs to the live epoch and is dropped (§7.4) — observable, not silent
                ResolveLogger()?.LogDebug($"{@event}: trigger for an unknown event type ignored");
                return;
            }

            foreach (var name in type.EffectiveCancels)       // lock-free coherent snapshot
                FindEventType(name)?.CancelActions();

            if (!ShouldTriggerEvent(@event))
                return;

            @event.Source = this;

            foreach (var context in HarvestContext())
                @event.AddContext(context);

            type.EnsureAnchor();                              // direct in-class assignment (Ping = null)
                                                              // may have replaced the chain — repair + warn
            var entries = type.Entries().Cast<EventInvocation>().ToArray();

            var wasInPipeline = _inPipeline.Value;
            _inPipeline.Value = true;

            try
            {
                if (type.Descriptor.Parallel)
                {
                    var tasks = entries.Select(handler => handler(@event)).ToArray();

                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    catch
                    {
                        // WhenAll rethrows only the FIRST failure — every other entry's
                        // exception must never vanish silently: first propagates (as in
                        // sequential mode), the rest are surfaced in the log
                        Exception? first = null;

                        foreach (var task in tasks)
                        {
                            if (task.Exception?.InnerException is not Exception failure)
                                continue;

                            if (first == null)
                                first = failure;
                            else
                                ResolveLogger()?.LogError(failure, $"{@event}: additional parallel handler failure");
                        }

                        ExceptionDispatchInfo.Capture(first!).Throw();
                    }
                }
                else
                {
                    foreach (var handler in entries)
                        await handler(@event);
                }
            }
            finally
            {
                _inPipeline.Value = wasInPipeline;
            }

            OnEventTriggered(@event);
        }

        #endregion

        #region The tree (§5)

        /// <summary>Ordered parent edges (insertion order — bubbling is deterministic,
        /// unlike the legacy HashSet). Snapshot-on-read: edges mutate on observer
        /// threads while walks and inspection enumerate.</summary>
        internal EventMetaObject[] Parents
        {
            get { lock (_parentsLock) return [.. _parents]; }
        }

        /// <summary>Contract: attach/detach of the SAME edge pair must be sequenced by
        /// the caller (scope owners naturally are — the observer serializes under its
        /// mutex). The hooks fire outside the lock; unsequenced concurrent attach+detach
        /// of one pair could deliver them out of order.</summary>
        internal void AttachParent(EventMetaObject parent)
        {
            ArgumentNullException.ThrowIfNull(parent);

            lock (_parentsLock)
            {
                if (_parents.Contains(parent))
                    return;                                   // idempotent, like the legacy Monitors set

                _parents.Add(parent);
            }

            OnAttachedTo(parent);
        }

        internal void DetachParent(EventMetaObject parent)
        {
            bool removed;

            lock (_parentsLock)
                removed = _parents.Remove(parent);

            if (removed)
                OnDetachedFrom(parent);
        }

        #endregion

        #region Action dispatch & error routing (engine-internal seams)

        internal ActionHandler? GetActionHandler(string name)
            => _actionHandlers.TryGetValue(name, out var handler) ? handler : null;

        internal ActionHandler? GetURLActionHandler(string scheme)
            => _urlActionHandlers.TryGetValue(scheme, out var handler) ? handler : null;

        /// <summary>The string-keyed handle for events INHERITED from a base class —
        /// C# lets a field-like event be used as a delegate only inside its declaring
        /// class, so derived code writes <c>GetEvent(nameof(Idle)).AddAction(...)</c>.</summary>
        protected EventType Event(string eventName) => GetEventType(eventName);

        /// <summary>
        /// Offers a foreign event's action to this object, resolving through the FULL
        /// tree walk including the root fallback — the seam plugins use to dispatch on
        /// behalf of another object.
        /// </summary>
        public Task<bool> TryHandleEventAction(Event eventRef, EventAction action) => DispatchActionAsync(eventRef, action);

        /// <summary>The SELF step only — used by <see cref="ActionManager"/> to iterate
        /// its providers without recursing back into the root fallback.</summary>
        internal Task<bool> DispatchSelfAsync(Event eventRef, EventAction action) => DispatchActionCoreAsync(eventRef, action);

        /// <summary>The SELF step of the resolution walk: executes this object's own
        /// [ActionHandler] or [URLActionHandler], if any — the two kinds resolve against
        /// their own registries (§6.4) and are never confused, but share the invocation
        /// machinery (concurrency gate, context binding, error routing).</summary>
        private protected virtual async Task<bool> DispatchActionCoreAsync(Event @event, EventAction action)
        {
            ActionHandler? handler;
            IReadOnlyList<object>? arguments;
            object[] context;

            if (action is URLEventAction urlAction)
            {
                handler = GetURLActionHandler(urlAction.Url.Scheme);
                arguments = null;                             // a URL has no parameter list —
                context = [urlAction.Url, .. @event.Context]; // it binds as context instead
            }
            else if (action is JSEventAction jsAction)
            {
                handler = GetActionHandler(jsAction.Name);
                arguments = jsAction.Arguments;
                context = [.. @event.Context];
            }
            else
            {
                return false;                                 // unknown kinds walk on (open hierarchy)
            }

            if (handler is null)
                return false;

            if (!handler.TryBeginInvocation())
                return true;                     // non-concurrent handler already running → skip silently

            try
            {
                // PrepareWithContext sits inside the routed region: an argument-conversion
                // failure surfaces through the error chain like an execution failure
                try
                {
                    if (handler.PrepareWithContext(this, arguments, context) is ActionInvocation invocation)
                    {
                        ResolveLogger()?.LogDebug($"{@event} -> {action}" + (@event.Source != this ? $" @ {GetType().Name}" : ""));

                        await invocation.InvokeAsync();
                    }
                }
                catch (Exception ex)
                {
                    // §9.3 error streamlining: always unwrapped, surfaced exactly once —
                    // routed from the EXECUTING object through its tree walk to the root
                    if (ex is TargetInvocationException { InnerException: Exception inner })
                        ex = inner;

                    if (!RouteActionError(new ActionError(@event, action, ex) { Actor = this }))
                    {
                        ExceptionDispatchInfo.Capture(ex).Throw();
                    }
                }
            }
            finally
            {
                handler.EndInvocation();
            }

            return true;
        }

        /// <summary>Action resolution (§6.3): self → parents in order, recursively
        /// (cycle-safe) → root, consulted ONCE after the walk. An engine guarantee
        /// independent of tree membership — a node with no parents resolves at the root.
        /// The root is reachable through ANY visited node (origin first): an unattached
        /// child tracked by an attached monitor keeps root reachability, exactly like
        /// the legacy bubbling chains did.</summary>
        internal async Task<bool> DispatchActionAsync(Event @event, EventAction action)
        {
            List<EventMetaObject> visited = [];

            if (await DispatchThroughTreeAsync(@event, action, visited))
                return true;

            if (ResolveRootVia(visited) is IEventSystemRoot root)
                return await root.TryHandleEventAction(@event, action);

            return false;
        }

        private async Task<bool> DispatchThroughTreeAsync(Event @event, EventAction action, List<EventMetaObject> visited)
        {
            if (visited.Contains(this))
                return false;

            visited.Add(this);

            @event.ParentContext.Push(this);

            try
            {
                if (await DispatchActionCoreAsync(@event, action))
                    return true;

                foreach (var parent in Parents)
                {
                    if (await parent.DispatchThroughTreeAsync(@event, action, visited))
                        return true;
                }

                return false;
            }
            finally
            {
                @event.ParentContext.Pop();
            }
        }

        private static IEventSystemRoot? ResolveRootVia(List<EventMetaObject> visited)
        {
            foreach (var node in visited)
            {
                if (node.RootAccessor?.Invoke() is IEventSystemRoot root)
                    return root;
            }

            return null;
        }

        /// <summary>The SELF step of the error walk — kept virtual for engine hosts
        /// that need a different self step (no production overrides).</summary>
        private protected virtual bool RouteActionErrorCore(ActionError error) => OnActionError(error);

        /// <summary>Error routing walks the same path as action resolution (§6.3):
        /// self → parents recursively → root as the final handler.</summary>
        internal bool RouteActionError(ActionError error)
        {
            List<EventMetaObject> visited = [];

            if (RouteErrorThroughTree(error, visited))
                return true;

            if (ResolveRootVia(visited) is IEventSystemRoot root)
                return root.HandleActionError(error);

            return false;
        }

        private bool RouteErrorThroughTree(ActionError error, List<EventMetaObject> visited)
        {
            if (visited.Contains(this))
                return false;

            visited.Add(this);

            if (RouteActionErrorCore(error))
                return true;

            foreach (var parent in Parents)
            {
                if (parent.RouteErrorThroughTree(error, visited))
                    return true;
            }

            return false;
        }

        internal void ReportLostActionError(ActionError error)
        {
            ResolveLogger()?.LogError(error.Exception, $"{error.Event} -> {error.Action}: unhandled on the scheduled path");
        }

        internal void ReportBypassedInvocation(EventType type, Event @event)
        {
            ResolveLogger()?.LogWarning($"{@event}: raw delegate invocation of '{type.Name}' bypassed the event pipeline");
        }

        internal void ReportAnchorReseeded(EventType type)
        {
            ResolveLogger()?.LogWarning($"{GetType().Name}.{type.Name}: direct assignment replaced the invocation list — engine anchor re-seeded");
        }

        internal void ReportRefusedArming(EventType type)
        {
            ResolveLogger()?.LogWarning($"{GetType().Name}.{type.Name}: pending refused — the owner is disposed or the binding removed; was the event meant to be orphaned?");
        }

        internal ILogger? ResolveLogger()
        {
            if (!_loggerResolved)
            {
                _logger = (ILogger?)GetType().GetAllFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(f => typeof(ILogger).IsAssignableFrom(f.FieldType)).FirstOrDefault()?.GetValue(this);

                _loggerResolved = _logger != null;            // required-init loggers are assigned AFTER
            }                                                 // the ctor — never latch a null lookup

            return _logger;
        }

        #endregion

        public virtual void Dispose()
        {
            IsEngineDisposed = true;                          // arming refused from here on — a trigger
                                                              // racing the sweep cannot revive a pending
            EventTypeBase[] types;
            lock (_eventsLock)
                types = [.. _events.Values];

            foreach (var type in types)
                (type as EventType)?.CancelActions();

            foreach (var parent in Parents)                   // disposal backstop: no dead node stays
                DetachParent(parent);                         // in the tree (idempotent)
        }

        #region Per-type reflection metadata

        private sealed record TypeMetadata(EventRecord[] Events, EventRecord[] Filters,
            Dictionary<string, MethodInfo> Handlers, Dictionary<string, MethodInfo> URLHandlers,
            PropertyInfo[] ContextProperties);

        private sealed record EventRecord(EventInfo Info, FieldInfo Field, EventDescriptor Descriptor);

        private static TypeMetadata BuildMetadata(Type type)
        {
            List<EventRecord> events = [], filters = [];

            foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var delegateName = eventInfo.EventHandlerType?.Name ?? "";

                bool isEvent = delegateName.Contains("EventInvocation");
                bool isFilter = delegateName.StartsWith("EventFilter");

                if (!isEvent && !isFilter)
                    continue;                                 // non-event-system delegates stay invisible

                if (isEvent && eventInfo.EventHandlerType != typeof(EventInvocation))
                    throw new NotSupportedException(          // a registered-but-dead event would be worse
                        $"{type.Name}.{eventInfo.Name}: generic EventInvocation<T> events are not supported — declare the member as 'event EventInvocation'");

                var field = type.GetAllFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name == eventInfo.Name)
                    ?? throw new InvalidOperationException(
                        $"{type.Name}.{eventInfo.Name}: no backing field — explicit-accessor events are not supported by the event system");

                var record = new EventRecord(eventInfo, field, BuildDescriptor(eventInfo));

                (isEvent ? events : filters).Add(record);
            }

            Dictionary<string, MethodInfo> handlers = [];
            Dictionary<string, MethodInfo> urlHandlers = new(StringComparer.OrdinalIgnoreCase);   // URL schemes are
            HashSet<MethodInfo> handlerRoots = [];                                                // case-insensitive

            foreach (var method in type.GetAllMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<ActionHandlerAttribute>() is ActionHandlerAttribute attribute)
                {
                    // overrides of an annotated virtual inherit the attribute and appear
                    // once per hierarchy level — dedupe by base definition, keeping the
                    // most-derived declaration (GetAllMethods yields derived-first)
                    if (!handlerRoots.Add(method.GetBaseDefinition()))
                        continue;

                    // [URLActionHandler] IS an [ActionHandler] (same declaration surface),
                    // but the two registries stay separate — a scheme never shadows a name
                    var registry = attribute is URLActionHandlerAttribute ? urlHandlers : handlers;

                    if (!registry.TryAdd(attribute.Name, method))
                        throw new ArgumentException($"{type.Name}: duplicate [{attribute.GetType().Name[..^"Attribute".Length]}(\"{attribute.Name}\")]");
                }
            }

            var contextProperties = type.GetAllProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<EventContextAttribute>() != null).ToArray();

            return new TypeMetadata([.. events], [.. filters], handlers, urlHandlers, contextProperties);
        }

        private static EventDescriptor BuildDescriptor(EventInfo eventInfo)
        {
            var options = eventInfo.GetCustomAttribute<EventOptionsAttribute>();

            return new EventDescriptor
            {
                Opposites = [.. eventInfo.GetCustomAttributes<EventOppositeAttribute>().SelectMany(a => a.Opposites).Distinct()],
                Cancels = [.. eventInfo.GetCustomAttributes<EventCancelsAttribute>().SelectMany(a => a.Cancels).Distinct()],
                Concurrent = options?.Concurrent ?? false,
                Parallel = options?.Parallel ?? false,
            };
        }

        #endregion
    }
}
