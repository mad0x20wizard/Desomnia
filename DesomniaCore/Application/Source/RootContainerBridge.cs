using Autofac;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Core.Lifetime;
using Autofac.Features.Decorators;

namespace MadWizard.Desomnia
{
    /// <summary>
    /// Bridges the persistent container into a per-configuration application container.
    /// Every service a module registered in <see cref="Module.LoadOnce(ContainerBuilder, Microsoft.Extensions.Configuration.IConfiguration)"/>
    /// is answered dynamically: resolution delegates to the persistent container, and the bridged
    /// registrations are externally owned, so a configuration rebuild never disposes the persistent
    /// instances or the OS state they hold. Because this is a registration source, build-time gates
    /// (<c>OnlyIf(reg =&gt; reg.IsRegistered(...))</c>) see the persistent services too.
    /// <para>What crosses the bridge is decided per request by an <c>export</c> predicate over the
    /// requested service type. The persistent container is now the persistent host's own container,
    /// so it also holds the host's framework services (hosting, logging, options) and Autofac's own
    /// relationship (<c>IEnumerable&lt;T&gt;</c>, <c>Func&lt;T&gt;</c>) and self registrations; the
    /// predicate keeps those behind (each application build runs its own), and exports only the
    /// modules' services. No frozen snapshot: the predicate is re-applied on every resolve.</para>
    /// </summary>
    internal sealed class RootContainerBridge : IRegistrationSource
    {
        // Autofac's internal collection-ordering key: bridged registrations inherit the
        // persistent registration's sequence number, so application-side IEnumerable<T>
        // keeps the original registration order (and bridged items sort before app-side
        // ones, like the persistent scope registers before the modules).
        private const string RegistrationOrderMetadataKey = "__RegistrationOrder";

        private readonly ILifetimeScope _container;
        private readonly Func<Type, bool> _export;

        internal RootContainerBridge(ILifetimeScope root) : this(root, ExportsModuleServices) { }

        internal RootContainerBridge(ILifetimeScope root, Func<Type, bool> export)
        {
            ValidateLifetimes(root, export);

            _container = root;
            _export = export;
        }

        /// <summary>Rejects the lifetimes the persistent container cannot carry. An owned
        /// DISPOSABLE transient: every resolve would be tracked by the persistent root's
        /// disposer until process exit — an unbounded leak. A per-scope lifetime: its
        /// services resolve both bridged (on the root) and in short-lived condition scopes,
        /// so the instances would silently diverge. Singletons, externally-owned
        /// registrations (the module's business) and untracked non-disposable transients
        /// (e.g. environment conditions, interface matchers) are all fine. Only the exported
        /// (module) registrations are constrained — the host's framework services are exempt.
        /// This check is static best effort — a delegate registration only reveals its declared
        /// type, so the bridge backstops it against the actual instances at resolve time.</summary>
        internal static void ValidateLifetimes(ILifetimeScope container)
            => ValidateLifetimes(container, ExportsModuleServices);

        internal static void ValidateLifetimes(ILifetimeScope container, Func<Type, bool> export)
        {
            foreach (var registration in container.ComponentRegistry.Registrations)
            {
                if (!registration.Services.OfType<IServiceWithType>().Any(service => export(service.ServiceType)))
                    continue;

                // A decorator registration reads as an owned disposable transient, but is not one:
                // its instances follow the ownership of the registration they decorate – measured
                // against Autofac 9.3.1, a decorator over an ExternallyOwned service is not
                // tracked. The decorated registration is validated on its own above, so the
                // decorator adds nothing the check does not already see.
                if (registration.Services.Any(service => service is DecoratorService))
                    continue;

                if (registration.Sharing == InstanceSharing.Shared)
                {
                    if (registration.Lifetime is RootScopeLifetime)
                        continue;

                    throw new InvalidOperationException(
                        $"Persistent registration {registration} has a per-scope lifetime, which the " +
                        $"persistent container cannot honor: its services resolve from the root (bridged " +
                        $"into the application) and from short-lived condition scopes, and the instances " +
                        $"would diverge. Register it .SingleInstance() or .InstancePerDependency() in LoadOnce.");
                }

                if (registration.Ownership == InstanceOwnership.ExternallyOwned)
                    continue;

                var limit = registration.Activator.LimitType;

                if (!typeof(IDisposable).IsAssignableFrom(limit) && !typeof(IAsyncDisposable).IsAssignableFrom(limit))
                    continue;

                throw new InvalidOperationException(
                    $"Persistent registration {registration} is a disposable transient: every resolved " +
                    $"instance would be tracked by the persistent container until process exit. " +
                    $"Register it .SingleInstance() in LoadOnce (or .ExternallyOwned() when the " +
                    $"module manages the instances itself).");
            }
        }

        /// <summary>The default export policy: only the modules' own services cross the bridge into
        /// the inner container. Everything framework — the hosting lifetime and hosted services,
        /// logging, options, Autofac's own relationship (<c>IEnumerable&lt;T&gt;</c>,
        /// <c>Func&lt;T&gt;</c>) and self (<see cref="ILifetimeScope"/>, <see cref="IComponentContext"/>)
        /// registrations, and <see cref="IStartable"/> (which each inner build would start again) —
        /// lives in a <c>System</c>/<c>Microsoft</c>/<c>Autofac</c> namespace and stays behind, so
        /// the inner host runs its own.</summary>
        internal static bool ExportsModuleServices(Type serviceType)
        {
            var ns = serviceType.Namespace;

            return ns is not null
                && !ns.StartsWith("MadWizard.Desomnia.Application.Lifetime", StringComparison.Ordinal)
                && !ns.StartsWith("System", StringComparison.Ordinal)
                && !ns.StartsWith("Microsoft", StringComparison.Ordinal)
                && !ns.StartsWith("Autofac", StringComparison.Ordinal);
        }

        // a registration crosses the bridge if any of its services is exported; a registration all
        // of whose services are framework/relationship types stays private to the persistent host
        private bool IsExported(IComponentRegistration registration)
            => registration.Services.OfType<IServiceWithType>().Any(service => _export(service.ServiceType));

        bool IRegistrationSource.IsAdapterForIndividualComponents => false;

        IEnumerable<IComponentRegistration> IRegistrationSource.RegistrationsFor(Service service, Func<Service, IEnumerable<ServiceRegistration>> registrationAccessor)
        {
            if (service is not IServiceWithType typed || !_export(typed.ServiceType))
                yield break;

            var limitType = typed.ServiceType;

            // one bridge per persistent registration, so multi-registrations keep their
            // IEnumerable<T> semantics and their metadata on the application side; the
            // default-first enumeration order also makes the persistent default the
            // application-side default (the first source registration claims it)
            foreach (var target in _container.ComponentRegistry.ServiceRegistrationsFor(service))
            {
                if (!IsExported(target.Registration))
                    continue;

                var registration = RegistrationBuilder
                    .ForDelegate(limitType, (_, parameters) =>
                    {
                        var instance = _container.ResolveComponent(new ResolveRequest(service, target, parameters));

                        // backstop for what ValidateLifetimes cannot see statically (a delegate
                        // registration only declares its interface): an owned disposable
                        // transient resolved through the bridge is tracked by the persistent
                        // root until process exit — fail loudly instead of leaking silently
                        if (instance is IDisposable or IAsyncDisposable
                            && target.Registration.Sharing == InstanceSharing.None
                            && target.Registration.Ownership == InstanceOwnership.OwnedByLifetimeScope)
                        {
                            throw new InvalidOperationException(
                                $"Persistent registration {target.Registration} produced a disposable transient " +
                                $"({instance.GetType().Name}): every bridged resolve would be tracked by the " +
                                $"persistent container until process exit. Register it .SingleInstance() in " +
                                $"LoadOnce (or .ExternallyOwned() when the module manages the instances itself).");
                        }

                        return instance;
                    })
                    .WithMetadata(target.Registration.Metadata.Where(entry => !entry.Key.StartsWith("__")))
                    .ExternallyOwned()
                    .As(service)
                    .CreateRegistration();

                // overwrite (never pre-set — CreateRegistration adds its own) the ordering
                // key with the persistent sequence number, restoring registration order in
                // application-side collections
                if (target.Registration.Metadata.TryGetValue(RegistrationOrderMetadataKey, out var order))
                    registration.Metadata[RegistrationOrderMetadataKey] = order;

                yield return registration;
            }
        }
    }
}
