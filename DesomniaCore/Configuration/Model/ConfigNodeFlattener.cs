using MadWizard.Desomnia.Configuration.Binding;

namespace MadWizard.Desomnia.Configuration.Model
{
    /// <summary>
    /// Turns a <see cref="ConfigNode"/> tree into flat configuration key/value pairs — the
    /// same DATA SHAPE the stock JSON/YAML providers produce, so the configuration format
    /// stays uniform regardless of the file format serving it:
    ///
    /// <list type="bullet">
    /// <item>the root element's name is not part of any key; its attributes and children
    ///   become the top-level keys</item>
    /// <item>an element's text content becomes the value at the element's own path (which can
    ///   coexist with child keys)</item>
    /// <item>the items of an <see cref="CollectionKind.Array"/> collection are keyed by their
    ///   0-based document-order index — like JSON array elements. A written name attribute is
    ///   an ordinary attribute of the item (it binds into the item's Name property) and NEVER
    ///   part of the key: the key stays an implementation detail</item>
    /// <item>the items of a <see cref="CollectionKind.Dictionary"/> collection are keyed by
    ///   their name attribute — like JSON object members; every item must carry one</item>
    /// <item>an element that is no known collection flattens as a singular object; several
    ///   same-named siblings of such an element (an unknown collection, e.g. of a plugin that
    ///   is not loaded) fall back to the 0-based index keys, so an open-format file always
    ///   flattens without collisions</item>
    /// </list>
    ///
    /// This layout deliberately DIVERGES from the stock <c>XmlConfigurationProvider</c>, which
    /// splices the name attribute into the key path: there, the name leaks into the data
    /// layout (and, synthesized, into the bound Name property). Here the name is pure user
    /// data — <c>Name</c> binds null when none is written.
    ///
    /// The pairs come out in document order, and the providers preserve that order into
    /// <c>IConfiguration.GetChildren()</c>.
    /// </summary>
    internal static class ConfigNodeFlattener
    {
        public static IReadOnlyList<KeyValuePair<string, string?>> Flatten(ConfigNode root, CollectionElements collections)
        {
            List<KeyValuePair<string, string?>> data = [];
            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

            void Emit(string key, string? value)
            {
                if (!keys.Add(key))
                    throw new ConfigurationValueException($"Duplicate configuration key '{key}'.");

                data.Add(new KeyValuePair<string, string?>(key, value));
            }

            FlattenElement(root, prefix: string.Empty, collections, Emit);

            return data;
        }

        private static void FlattenElement(ConfigNode element, string prefix,
            CollectionElements collections, Action<string, string?> emit)
        {
            // the element's own value; the root element has no key of its own
            if (element.Value is string value && prefix.Length > 0)
                emit(prefix, value);

            foreach (var attribute in element.Children.Where(child => child.Kind == ConfigNodeKind.Attribute))
                emit(Combine(prefix, attribute.Name), attribute.Value);

            var children = element.Children.Where(child => child.Kind == ConfigNodeKind.Element).ToList();

            // how many siblings share an element name - an unknown collection (several
            // same-named siblings of an unregistered element) falls back to index keys
            Dictionary<string, int>? groupSizes = null;

            if (children.Count > 1)
            {
                groupSizes = new(StringComparer.OrdinalIgnoreCase);

                foreach (var child in children)
                    groupSizes[child.Name] = groupSizes.GetValueOrDefault(child.Name) + 1;
            }

            // the running 0-based document-order index per element name
            Dictionary<string, int>? indices = null;

            int NextIndex(string name)
            {
                indices ??= new(StringComparer.OrdinalIgnoreCase);
                int index = indices.GetValueOrDefault(name);
                indices[name] = index + 1;

                return index;
            }

            foreach (var child in children)
            {
                var path = Combine(prefix, child.Name);

                switch (collections.KindOf(child.Name))
                {
                    case CollectionKind.Array:
                        path = Combine(path, NextIndex(child.Name).ToString());
                        break;

                    case CollectionKind.Dictionary:
                        path = Combine(path, child.ItemName ?? throw new ConfigurationValueException(
                            $"<{child.Name}> is a dictionary collection; every item needs a name attribute (at '{path}')."));
                        break;

                    default: // singular - unless same-named siblings make it an unknown collection
                        if (groupSizes?.GetValueOrDefault(child.Name) > 1)
                            path = Combine(path, NextIndex(child.Name).ToString());
                        break;
                }

                FlattenElement(child, path, collections, emit);
            }
        }

        private static string Combine(string prefix, string segment)
            => prefix.Length == 0 ? segment : $"{prefix}:{segment}";
    }
}
