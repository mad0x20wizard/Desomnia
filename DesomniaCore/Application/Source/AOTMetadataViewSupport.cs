using Autofac;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Features.Metadata;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace MadWizard.Desomnia
{
    /// <summary>
    /// AOT-safe replacement for Autofac's strongly-typed metadata views.
    ///
    /// Autofac builds a <see cref="Meta{TDependency, TMetadata}"/> view by calling
    /// <c>MetadataViewProvider.GetMetadataValue&lt;TProperty&gt;</c> via <c>MakeGenericMethod</c> for each
    /// metadata property. NativeAOT cannot JIT that for value-type properties (e.g. <c>int Order</c>), so it
    /// throws "missing native code" at runtime.
    ///
    /// This source instead provides the whole <c>IEnumerable&lt;Meta&lt;A,B&gt;&gt;</c> itself: it resolves the
    /// loosely-typed <see cref="Meta{T}"/> (value + string-keyed dictionary, which is AOT-safe) and builds the
    /// B view by plain reflection (<c>SetValue</c>, no dynamic code). Because it reports itself as a
    /// non-adapter source that supplies the collection directly, it shadows the built-in collection source
    /// (verified: no duplicates, resolving at the root or in a child scope). Consumers keep using
    /// <c>Meta&lt;A,B&gt;</c> unchanged; this source is registered only when dynamic code is unavailable
    /// (<c>!RuntimeFeature.IsDynamicCodeSupported</c> in <c>ApplicationBuilder.ConfigureContainer</c>).
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Meta<A,B> and the metadata view types are reference-type instantiations declared " +
                        "statically by the consumers (so the closed generics are compiled in) and kept by the " +
                        "trimmer root descriptor; MakeGenericType over reference types is AOT-safe.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Metadata view types are preserved whole via the trimmer root descriptor (preserve=all).")]
    [UnconditionalSuppressMessage("Trimming", "IL2062",
        Justification = "The runtime Meta<A,B> type comes from a statically-declared consumer service, kept by the trimmer root descriptor.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Metadata view types are preserved whole via the trimmer root descriptor (preserve=all).")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Metadata view types are preserved whole via the trimmer root descriptor (preserve=all).")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Metadata view types are preserved whole via the trimmer root descriptor (preserve=all).")]
    internal sealed class AOTMetadataViewSupport : IRegistrationSource
    {
        public bool IsAdapterForIndividualComponents => false;

        public IEnumerable<IComponentRegistration> RegistrationsFor(
            Service service, Func<Service, IEnumerable<ServiceRegistration>> registrationAccessor)
        {
            // Only handle IEnumerable<Meta<A, B>> requests.
            if (service is not IServiceWithType typed) yield break;

            var enumerableType = typed.ServiceType;
            if (!enumerableType.IsGenericType || enumerableType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                yield break;

            var metaType = enumerableType.GetGenericArguments()[0];
            if (!metaType.IsGenericType || metaType.GetGenericTypeDefinition() != typeof(Meta<,>))
                yield break;

            var serviceType  = metaType.GetGenericArguments()[0];
            var metadataType = metaType.GetGenericArguments()[1];

            var looseMeta       = typeof(Meta<>).MakeGenericType(serviceType);
            var looseEnumerable = typeof(IEnumerable<>).MakeGenericType(looseMeta);
            var resultList      = typeof(List<>).MakeGenericType(metaType);
            var valueProperty    = looseMeta.GetProperty(nameof(Meta<object>.Value))!;
            var metadataProperty = looseMeta.GetProperty(nameof(Meta<object>.Metadata))!;

            var registration = RegistrationBuilder.ForDelegate(enumerableType, (context, _) =>
            {
                var source = (IEnumerable) context.Resolve(looseEnumerable);
                var result = (IList) Activator.CreateInstance(resultList)!;

                foreach (var loose in source)
                {
                    var value    = valueProperty.GetValue(loose);
                    var metadata = (IDictionary<string, object?>) metadataProperty.GetValue(loose)!;

                    result.Add(Activator.CreateInstance(metaType, value, BuildView(metadataType, metadata)));
                }

                return result;
            }).As(service);

            yield return registration.CreateRegistration();
        }

        // Builds a metadata "view" object from the string-keyed dictionary using plain reflection.
        // No MakeGenericMethod / MakeGenericType — AOT-safe as long as the view type is preserved.
        private static object BuildView(Type viewType, IDictionary<string, object?> metadata)
        {
            var view = Activator.CreateInstance(viewType)!;

            foreach (var property in viewType.GetProperties())
                if (property.CanWrite && metadata.TryGetValue(property.Name, out var value) && value is not null)
                    property.SetValue(view, value);

            return view;
        }
    }
}
