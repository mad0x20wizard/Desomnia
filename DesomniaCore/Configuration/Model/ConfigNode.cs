namespace MadWizard.Desomnia.Configuration.Model
{
    /// <summary>
    /// The abstract, representation-independent form of one configuration node — the currency
    /// of everything between the physical file and the <c>IConfiguration</c> handed to the
    /// host: the XML reader produces it, the environment merger folds trees of it, the
    /// flattener turns it into provider key/value data, and the effective-XML exporter
    /// serializes it back. Children keep document order (which the provider preserves all the
    /// way into <c>IConfiguration.GetChildren()</c>), and <see cref="Kind"/> remembers whether
    /// a leaf was written as an XML attribute or an element — a serialization hint only; the
    /// merger and the flattener treat both alike.
    /// </summary>
    internal sealed class ConfigNode(string name, ConfigNodeKind kind)
    {
        /// <summary>The node's name (an XML local name; namespaces are not part of the model).</summary>
        public string Name { get; } = name;

        /// <summary>How the node was physically written — the XML reconstruction hint.</summary>
        public ConfigNodeKind Kind { get; } = kind;

        /// <summary>
        /// The node's own value: an attribute's value, an element's text content, or the empty
        /// string for a bare element (<c>&lt;x/&gt;</c>) whose presence alone must bind (see
        /// the strict binder's empty-element rule). Null when the element carries no value —
        /// a node can have both a value AND children, a shape XML produces and
        /// <c>IConfiguration</c> supports (section value + child keys).
        /// </summary>
        public string? Value { get; set; }

        /// <summary>Child nodes in document order: attributes as they were written, then child elements.</summary>
        public List<ConfigNode> Children { get; } = [];

        /// <summary>Merge provenance (which environment block set this node, at which priority);
        /// null outside the merger. Not copied by <see cref="Clone"/>.</summary>
        public object? Origin { get; set; }

        public bool HasName(string name) => Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        /// <summary>The value of the "name" child attribute — the part of a node's identity that
        /// distinguishes collection items (see the merger and the flattener).</summary>
        public string? ItemName => Children.FirstOrDefault(child
            => child.Kind == ConfigNodeKind.Attribute && child.HasName(ItemNameAttribute))?.Value;

        public const string ItemNameAttribute = "name";

        /// <summary>Deep copy (without <see cref="Origin"/> annotations).</summary>
        public ConfigNode Clone()
        {
            var clone = new ConfigNode(Name, Kind) { Value = Value };

            foreach (var child in Children)
                clone.Children.Add(child.Clone());

            return clone;
        }

        public override string ToString()
            => $"{(Kind == ConfigNodeKind.Attribute ? "@" : "<")}{Name}{(ItemName is string name ? $" name={name}" : "")}{(Value is not null ? $" = \"{Value}\"" : "")}";
    }

    internal enum ConfigNodeKind
    {
        /// <summary>An XML element (may have a value, children, or both).</summary>
        Element,

        /// <summary>An XML attribute (always a leaf with a value).</summary>
        Attribute,
    }
}
