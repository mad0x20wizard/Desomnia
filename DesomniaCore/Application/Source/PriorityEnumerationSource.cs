using Autofac;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Features.Decorators;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace MadWizard.Desomnia
{
    public static class PriorityRegistrationExtensions
    {
        internal const string PriorityMetaKey = "Desomnia.Priority";

        /// <summary>
        /// Attaches ordering metadata to an arbitrary registration, defining its position whenever the
        /// component is resolved as part of a collection — <see cref="IEnumerable{T}"/>, <c>T[]</c>,
        /// <see cref="IList{T}"/>, ... (see <see cref="PriorityEnumerationSource"/>). Lower values come
        /// first; registrations without a priority default to 0 and keep their registration order.
        /// Nothing is required of the consuming side: it just resolves the collection.
        /// </summary>
        public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> WithPriority<TLimit, TActivatorData, TStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TStyle> registration, int priority)
                => registration.WithMetadata(PriorityMetaKey, priority);
    }

    /// <summary>
    /// Takes over Autofac's collection relationship so that <em>every</em> way of asking for "all
    /// implementations of T" — <see cref="IEnumerable{T}"/>, <c>T[]</c>, <see cref="IList{T}"/>,
    /// <see cref="ICollection{T}"/>, <see cref="IReadOnlyList{T}"/>, <see cref="IReadOnlyCollection{T}"/> —
    /// yields the components sorted by the priority attached to their registrations (see
    /// <see cref="PriorityRegistrationExtensions.WithPriority"/>). Registrations without a priority count
    /// as 0; equal priorities keep their registration order. Consuming code therefore declares nothing
    /// special: order is a property of the registration, not of the request. Registered once in
    /// <c>ApplicationBuilder.ConfigureApplication</c>.
    ///
    /// This mirrors Autofac's own <c>CollectionRegistrationSource</c> — same registration lookup, same
    /// per-item <see cref="ResolveRequest"/>, same result types (an array for <see cref="IEnumerable{T}"/>
    /// and <c>T[]</c>, a mutable <see cref="List{T}"/> for the list and collection interfaces) — so
    /// decorators, parameters and service keys behave exactly as they do without this source; only the
    /// sort key differs. Three details it has to get right:
    ///
    /// <list type="bullet">
    /// <item>Sources added later are consulted first, so registering this one after the container's
    /// default adapters is what makes it answer instead of the built-in source.</item>
    /// <item><see cref="IComponentRegistry.ServiceRegistrationsFor"/> yields the <em>newest registration
    /// first</em>, so the registration-order tie-break has to be explicit — a stable sort alone would
    /// reverse everything that shares a priority.</item>
    /// <item>A decorator is resolved through a <see cref="DecoratorService"/> carrying the decorated
    /// type, and an "any key" query needs the built-in source's adapter de-duplication; both are left
    /// alone.</item>
    /// </list>
    ///
    /// Resolving the components through the activation-time context (rather than the accessor handed to
    /// <see cref="RegistrationsFor"/>) is what lets registrations made in a child scope — which is where
    /// the network/host/service scopes put their discoveries and services — take part in the ordering.
    ///
    /// Two things deliberately stay with the built-in source: collections of value types (no shared
    /// native code under AOT), and Autofac's relationship wrappers — a <c>Meta&lt;T&gt;</c> adapter
    /// registration carries no priority of its own, so <c>IEnumerable&lt;Meta&lt;T&gt;&gt;</c> stays in
    /// registration order.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "The item type is always a reference type (value-type collections are left to the " +
                        "built-in source), so both List<T> and the array type are shared native code.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "List<T> is closed over reference types only, and lives in the BCL which the trimmer keeps.")]
    internal sealed class PriorityEnumerationSource : IRegistrationSource
    {
        /// <summary>The interfaces Autofac answers with a <see cref="List{T}"/> rather than an array.</summary>
        private static readonly HashSet<Type> ListTypes =
        [
            typeof(IList<>), typeof(ICollection<>),
            typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>)
        ];

        public bool IsAdapterForIndividualComponents => false;

        public IEnumerable<IComponentRegistration> RegistrationsFor(
            Service service, Func<Service, IEnumerable<ServiceRegistration>> registrationAccessor)
        {
            if (service is not IServiceWithType typed || service is DecoratorService) yield break;

            // an "any key" collection needs the built-in source, which knows to drop the any-key adapters
            // it would otherwise count twice
            if (service is KeyedService keyed && KeyedService.IsAnyKey(keyed.ServiceKey)) yield break;

            var collectionType = typed.ServiceType;
            var genericType = collectionType.IsGenericType ? collectionType.GetGenericTypeDefinition() : null;

            Type itemType;
            bool asList = false;

            if (collectionType.IsArray)
                itemType = collectionType.GetElementType()!;
            else if (genericType == typeof(IEnumerable<>))
                itemType = collectionType.GetGenericArguments()[0];
            else if (genericType is not null && ListTypes.Contains(genericType))
                (itemType, asList) = (collectionType.GetGenericArguments()[0], true);
            else
                yield break;

            if (itemType.IsValueType) yield break;

            var itemService = typed.ChangeType(itemType); // keeps a service key intact
            var limitType = asList ? typeof(List<>).MakeGenericType(itemType) : itemType.MakeArrayType();

            var registration = RegistrationBuilder.ForDelegate(limitType, (context, parameters) =>
            {
                var components = context.ComponentRegistry.ServiceRegistrationsFor(itemService)
                    .Where(component => !component.Registration.Options.HasFlag(RegistrationOptions.ExcludeFromCollections))
                    .OrderBy(PriorityOf)
                    .ThenBy(component => component.GetRegistrationOrder())
                    .ToList();

                if (asList)
                {
                    var list = (IList) Activator.CreateInstance(limitType, components.Count)!;

                    foreach (var component in components)
                        list.Add(Activate(context, itemService, component, parameters));

                    return list;
                }

                var items = Array.CreateInstance(itemType, components.Count);

                for (int i = 0; i < components.Count; i++)
                    items.SetValue(Activate(context, itemService, components[i], parameters), i);

                return items;
            }).As(service);

            yield return registration.CreateRegistration();
        }

        private static object Activate(IComponentContext context, Service service,
            ServiceRegistration component, IEnumerable<Parameter> parameters)
                => context.ResolveComponent(new ResolveRequest(service, component, parameters));

        private static int PriorityOf(ServiceRegistration component)
            => component.Registration.Metadata.TryGetValue(PriorityRegistrationExtensions.PriorityMetaKey, out var value)
                   && value is int priority ? priority : 0;
    }
}
