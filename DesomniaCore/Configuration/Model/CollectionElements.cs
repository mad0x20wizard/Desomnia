using MadWizard.Desomnia.Configuration.Binding;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Configuration.Model
{
    /// <summary>How the items of a collection element are keyed in the configuration data.</summary>
    public enum CollectionKind
    {
        /// <summary>A list/array-like collection: items are keyed by their 0-based document
        /// order index, like the stock JSON/YAML providers key array elements. A written
        /// name attribute stays an ordinary attribute of the item — never a key.</summary>
        Array,

        /// <summary>A string-keyed dictionary: the item's name attribute IS the key,
        /// like a JSON object member. Every item must carry one.</summary>
        Dictionary
    }

    /// <summary>
    /// Knows which element names form collections of complex items — and of which
    /// <see cref="CollectionKind"/> — knowledge derived once from the configuration types of
    /// the registered modules (see <see cref="Derive"/>) and immutable from then on. It is
    /// needed wherever the configuration's SHAPE is interpreted: XML cannot express whether a
    /// lone element is a singular object or a collection of one item, so the flattener and the
    /// environment merger consult this map. A format with an explicit collection syntax
    /// (JSON arrays, YAML sequences) needs none of this — the knowledge is an XML-only
    /// concern, which is why it lives here and not in the module API.
    /// </summary>
    public sealed class CollectionElements
    {
        /// <summary>Treats every element as singular — for sources used standalone,
        /// before (or without) any module-derived knowledge.</summary>
        public static readonly CollectionElements Empty = new();

        readonly Dictionary<string, CollectionKind> _elements = new(StringComparer.OrdinalIgnoreCase);

        private CollectionElements() { }

        public bool IsCollectionElement(string elementName) => _elements.ContainsKey(elementName);

        /// <summary>The collection kind of the given element name — null for an element
        /// that is no (known) collection and therefore flattens as a singular object.</summary>
        public CollectionKind? KindOf(string elementName)
            => _elements.TryGetValue(elementName, out var kind) ? kind : null;

        /// <summary>
        /// Walks the given configuration types and records the names of all properties holding
        /// collections of complex items: a string-keyed dictionary property is a
        /// <see cref="CollectionKind.Dictionary"/>, every other collection of complex items an
        /// <see cref="CollectionKind.Array"/>. One element name must not be both.
        /// </summary>
        public static CollectionElements Derive(IEnumerable<Type> configTypes)
        {
            CollectionElements elements = new();
            HashSet<Type> visited = [];

            foreach (var type in configTypes)
                elements.CollectCollectionElements(type, visited);

            return elements;
        }

        /// <summary>Declares array-kind collection elements by name. Test seam — production
        /// knowledge is always derived from the configuration types (see <see cref="Derive"/>).</summary>
        internal static CollectionElements Of(params string[] elementNames)
        {
            CollectionElements elements = new();

            foreach (var name in elementNames)
                elements._elements.Add(name, CollectionKind.Array);

            return elements;
        }

        /// <summary>Declares dictionary-kind collection elements by name. Test seam.</summary>
        internal CollectionElements WithDictionaries(params string[] elementNames)
        {
            foreach (var name in elementNames)
                _elements.Add(name, CollectionKind.Dictionary);

            return this;
        }

        private void Record(string elementName, CollectionKind kind)
        {
            if (_elements.TryGetValue(elementName, out var existing))
            {
                if (existing != kind)
                    throw new ConfigurationValueException($"The element name '{elementName}' is declared as " +
                        $"both a {existing} and a {kind} collection by the configuration types; " +
                        "one element name must map to one collection kind.");

                return;
            }

            _elements.Add(elementName, kind);
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
                    // a string-keyed dictionary of complex items BEFORE the array probe: a
                    // dictionary also enumerates (as key/value pairs), but its items are keyed
                    if (FindDictionaryItemType(property.PropertyType) is Type dictionaryItemType)
                    {
                        Record(property.Name, CollectionKind.Dictionary);

                        CollectCollectionElements(dictionaryItemType, visited);
                    }
                    else if (FindComplexItemType(property.PropertyType) is Type itemType)
                    {
                        Record(property.Name, CollectionKind.Array);

                        CollectCollectionElements(itemType, visited);
                    }
                    else if (IsComplexType(property.PropertyType))
                    {
                        CollectCollectionElements(property.PropertyType, visited);
                    }
                }
            }
        }

        /// <returns>The value type, if the given type is a string-keyed dictionary of complex items.</returns>
        private static Type? FindDictionaryItemType(Type type)
        {
            IEnumerable<Type> candidates = type.GetInterfaces();
            if (type.IsInterface)
                candidates = candidates.Prepend(type);

            foreach (var candidate in candidates)
            {
                if (candidate.IsConstructedGenericType &&
                    (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                     candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
                {
                    var keyType = candidate.GenericTypeArguments[0];
                    var valueType = candidate.GenericTypeArguments[1];

                    return keyType == typeof(string) && IsComplexType(valueType) ? valueType : null;
                }
            }

            return null;
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
