using MadWizard.Desomnia.Configuration.Binding;

namespace MadWizard.Desomnia.Configuration.Model
{
    /// <summary>
    /// Turns a <see cref="ConfigNode"/> tree into flat configuration key/value pairs — the
    /// same key layout the stock <c>XmlConfigurationProvider</c> produces (verified by the
    /// compatibility tests), because every module's configuration type binds against it:
    ///
    /// <list type="bullet">
    /// <item>the root element's name is not part of any key; its attributes and children
    ///   become the top-level keys</item>
    /// <item>an element's text content becomes the value at the element's own path (which can
    ///   coexist with child keys)</item>
    /// <item>an element with a "name" attribute gets the name value as an extra path segment
    ///   (the attribute itself is also emitted as a child key)</item>
    /// <item>same-named siblings that share an identity get 0-based index segments</item>
    /// <item>a nameless element of a registered collection gets a synthesized name (see
    ///   <see cref="CollectionElementRegistry"/>), keeping items distinguishable from
    ///   attributes even when a collection has a single nameless item</item>
    /// </list>
    ///
    /// The pairs come out in document order, and the providers preserve that order into
    /// <c>IConfiguration.GetChildren()</c>.
    /// </summary>
    internal static class ConfigNodeFlattener
    {
        public static IReadOnlyList<KeyValuePair<string, string?>> Flatten(ConfigNode root, CollectionElementRegistry collections)
        {
            List<KeyValuePair<string, string?>> data = [];
            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

            void Emit(string key, string? value)
            {
                if (!keys.Add(key))
                    throw new ConfigurationValueException($"Duplicate configuration key '{key}'.");

                data.Add(new KeyValuePair<string, string?>(key, value));
            }

            FlattenElement(root, prefix: string.Empty, synthesizedName: null, collections, Emit);

            return data;
        }

        private static void FlattenElement(ConfigNode element, string prefix, string? synthesizedName,
            CollectionElementRegistry collections, Action<string, string?> emit)
        {
            // the element's own value; the root element has no key of its own
            if (element.Value is string value && prefix.Length > 0)
                emit(prefix, value);

            foreach (var attribute in element.Children.Where(child => child.Kind == ConfigNodeKind.Attribute))
                emit(Combine(prefix, attribute.Name), attribute.Value);

            // a synthesized collection name behaves like a written name attribute, so the
            // binder sees it as a child key too
            if (synthesizedName is not null)
                emit(Combine(prefix, ConfigNode.ItemNameAttribute), synthesizedName);

            var children = element.Children.Where(child => child.Kind == ConfigNodeKind.Element).ToList();

            // pass 1: each child's identity - its written name, or a synthesized one for
            // nameless items of registered collections (counting only the nameless ones)
            var identities = new (string? Name, bool Synthesized)[children.Count];
            Dictionary<string, uint>? counters = null;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (child.ItemName is string name)
                {
                    identities[i] = (name, false);
                }
                else if (collections.IsCollectionElement(child.Name))
                {
                    counters ??= new(StringComparer.OrdinalIgnoreCase);
                    counters.TryGetValue(child.Name, out uint nr);
                    counters[child.Name] = ++nr;

                    identities[i] = (collections.BuildName(child.Name, nr), true);
                }
            }

            // pass 2: same-named siblings sharing an identity need 0-based index segments
            var groupSizes = new Dictionary<(string, string?), int>(SiblingGroupComparer.Instance);

            for (int i = 0; i < children.Count; i++)
                groupSizes[Group(children[i], identities[i])] =
                    groupSizes.GetValueOrDefault(Group(children[i], identities[i])) + 1;

            Dictionary<(string, string?), int>? indices = null;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var (name, synthesized) = identities[i];

                var path = Combine(prefix, child.Name);

                if (name is not null)
                    path = Combine(path, name);

                if (groupSizes[Group(child, identities[i])] > 1)
                {
                    indices ??= new(SiblingGroupComparer.Instance);
                    int index = indices.GetValueOrDefault(Group(child, identities[i]));
                    indices[Group(child, identities[i])] = index + 1;

                    path = Combine(path, index.ToString());
                }

                FlattenElement(child, path, synthesized ? name : null, collections, emit);
            }
        }

        private static (string, string?) Group(ConfigNode child, (string? Name, bool) identity)
            => (child.Name, identity.Name);

        private static string Combine(string prefix, string segment)
            => prefix.Length == 0 ? segment : $"{prefix}:{segment}";

        /// <summary>Case-insensitive over both the element name and the item name, like the
        /// configuration key space itself.</summary>
        private sealed class SiblingGroupComparer : IEqualityComparer<(string Element, string? Name)>
        {
            public static readonly SiblingGroupComparer Instance = new();

            public bool Equals((string Element, string? Name) x, (string Element, string? Name) y)
                => string.Equals(x.Element, y.Element, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string Element, string? Name) obj)
                => HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Element),
                    obj.Name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
        }
    }
}
