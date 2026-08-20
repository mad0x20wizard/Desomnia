using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Application.Registry
{
    /// <summary>
    /// Builds the synthesized name for a nameless collection element. Use this when code
    /// relies on the synthesized name format (which is otherwise an implementation detail,
    /// defaulting to "{elementName}#{nr}").
    /// </summary>
    public delegate string CollectionNameBuilder(string elementName, uint nr);

    /// <summary>
    /// Knows which element names form collections of complex items — knowledge derived from
    /// the modules' configuration types, needed wherever the configuration's shape is
    /// interpreted: the flattener synthesizes name attributes for nameless collection items
    /// (keeping the provider's key layout deterministic), and the environment merger appends
    /// nameless collection items as distinct instances instead of merging them.
    /// </summary>
    public sealed class CollectionElementRegistry
    {
        readonly HashSet<string> _elements = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, CollectionNameBuilder> _nameBuilders = new(StringComparer.OrdinalIgnoreCase);

        public bool IsCollectionElement(string elementName) => _elements.Contains(elementName);

        public IReadOnlySet<string> ElementNames => _elements;

        /// <summary>The synthesized name for the nr-th nameless item of the given collection.</summary>
        public string BuildName(string elementName, uint nr)
            => _nameBuilders.TryGetValue(elementName, out var builder) ? builder(elementName, nr) : $"{elementName}#{nr}";

        /// <summary>
        /// Registers an explicit name builder for nameless elements of the given collection
        /// (an explicit builder also marks the element as a collection).
        /// </summary>
        public void AddCollectionNameBuilder(string elementName, CollectionNameBuilder builder)
        {
            _nameBuilders[elementName] = builder;
            _elements.Add(elementName);
        }

        /// <summary>
        /// Walks the given configuration type and records the names of all properties holding
        /// collections of complex items. XML elements with these names are collection elements
        /// and get a synthesized name attribute if they don't carry one.
        /// </summary>
        public void AddCollectionElementsOf(Type configType)
        {
            CollectCollectionElements(configType, []);
        }

        private void CollectCollectionElements(Type type, HashSet<Type> visited)
        {
            if (IsFrameworkType(type) || !visited.Add(type))
                return;

            // run the type initializer, so custom TypeConverters registered there
            // (e.g. in a static constructor via TypeDescriptor.AddAttributes) take
            // effect before IsComplexType queries them
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);

            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
            {
                const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                foreach (var property in t.GetProperties(declared))
                {
                    if (FindComplexItemType(property.PropertyType) is Type itemType)
                    {
                        _elements.Add(property.Name);

                        CollectCollectionElements(itemType, visited);
                    }
                    else if (IsComplexType(property.PropertyType))
                    {
                        CollectCollectionElements(property.PropertyType, visited);
                    }
                }
            }
        }

        /// <returns>The item type, if the given type is a collection of complex items.</returns>
        private static Type? FindComplexItemType(Type type)
        {
            if (type == typeof(string) || type.IsArray)
                return null;

            IEnumerable<Type> candidates = type.GetInterfaces();
            if (type.IsInterface)
                candidates = candidates.Prepend(type);

            foreach (var candidate in candidates)
            {
                if (candidate.IsConstructedGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var itemType = candidate.GenericTypeArguments[0];

                    return IsComplexType(itemType) ? itemType : null;
                }
            }

            return null;
        }

        /// <returns>true, if the type binds by its children (attributes/elements) rather than from a string value.</returns>
        private static bool IsComplexType(Type type)
        {
            if (!(type.IsClass || type.IsInterface) || type == typeof(string) || IsFrameworkType(type))
                return false;

            return !TypeDescriptor.GetConverter(type).CanConvertFrom(typeof(string));
        }

        private static bool IsFrameworkType(Type type)
            => type.Namespace is string ns && (ns == "System" || ns.StartsWith("System.") || ns.StartsWith("Microsoft."));
    }
}
