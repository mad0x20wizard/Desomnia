using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using NLog;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Merges the &lt;SystemMonitor&gt; contents of all active environment blocks
    /// (in document order) into one effective configuration tree. Nodes are identified
    /// by their name plus their "name" attribute; nameless collection items (as derived
    /// from the modules' config types) are distinct instances and are appended instead
    /// of merged. An attribute and a same-named element merge as one value — the physical
    /// form is not part of a node's identity (the first block's form wins the written form,
    /// the priority rules decide the value).
    ///
    /// Conflicting values are decided by the blocks' priority - higher supersedes,
    /// regardless of document order. Between EQUAL priorities the onConflict setting
    /// applies: the later block wins (default), the earlier keeps its value, or the
    /// conflict aborts startup. Every merged node is annotated with its origin
    /// (block priority + name), since annotations are what makes this decidable
    /// after the fold has mixed several blocks into one tree.
    /// </summary>
    internal static class ConfigMerger
    {
        static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Provenance of a merged value: which environment set it, at which priority.</summary>
        private sealed record MergeOrigin(int Priority, string Environment);

        public static ConfigNode Merge(IEnumerable<EnvironmentBlock> blocks, CollectionElements collections, ConflictResolution onConflict)
        {
            ConfigNode? result = null;

            foreach (var block in blocks)
            {
                var origin = new MergeOrigin(block.Priority, block.DisplayName);

                if (result is null)
                    result = Annotate(block.Content.Clone(), origin);
                else
                    MergeNode(result, block.Content, origin, collections, onConflict);
            }

            return result ?? new ConfigNode(EnvironmentParser.SYSTEM_MONITOR_ELEMENT, ConfigNodeKind.Element);
        }

        private static void MergeNode(ConfigNode target, ConfigNode source, MergeOrigin origin, CollectionElements collections, ConflictResolution onConflict)
        {
            MergeValue(target, source, origin, onConflict);

            foreach (var child in source.Children)
            {
                // the "name" attribute is part of the node's identity, never merged as a value
                // (a matched target carries the same name by definition)
                if (child.Kind == ConfigNodeKind.Attribute && child.HasName(ConfigNode.ItemNameAttribute))
                    continue;

                var childItemName = child.ItemName;

                // nameless collection items are distinct instances - never merged
                if (child.Kind == ConfigNodeKind.Element && childItemName is null && collections.IsCollectionElement(child.Name))
                {
                    target.Children.Add(Annotate(child.Clone(), origin));
                    continue;
                }

                var match = target.Children.FirstOrDefault(node =>
                    node.HasName(child.Name) &&
                    string.Equals(node.ItemName, childItemName, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                    target.Children.Add(Annotate(child.Clone(), origin));
                else
                    MergeNode(match, child, origin, collections, onConflict);
            }
        }

        /// <summary>Merges the node's own value - an attribute's value and an element's text
        /// content alike (in the abstract representation both are just the value).</summary>
        private static void MergeValue(ConfigNode target, ConfigNode source, MergeOrigin origin, ConflictResolution onConflict)
        {
            if (source.Value is not string value)
                return;

            if (target.Value is not string existing)
            {
                target.Value = value;

                // real content carries its contributor's provenance into later conflicts (the
                // node's own annotation tracks its creator, which may be a different block);
                // a presence-only fill ("") asserts nothing worth defending
                if (value.Length > 0)
                    Reannotate(target, origin);

                return;
            }

            if (existing == value)
            {
                // no conflict - let the highest priority that asserted the value back it
                if (OriginOf(target).Priority < origin.Priority)
                    Reannotate(target, origin);

                return;
            }

            // presence-only values never win against real content: a bare <x/> reasserts the
            // node, it does not empty it (and real content fills a bare node without a conflict)
            if (value.Length == 0)
                return;

            if (existing.Length == 0 || Resolve(target, origin, onConflict, Describe(target), existing, value))
            {
                target.Value = value;

                Reannotate(target, origin);
            }
        }

        /// <summary>Decides a value conflict: higher priority always wins; equal priorities resolve per onConflict.</summary>
        private static bool Resolve(ConfigNode existing, MergeOrigin origin, ConflictResolution onConflict, string subject, string oldValue, string newValue)
        {
            var current = OriginOf(existing);

            if (origin.Priority > current.Priority)
            {
                Logger.Debug($"{subject} superseded by higher-priority environment '{origin.Environment}' ('{oldValue}' -> '{newValue}')");

                return true;
            }

            if (origin.Priority < current.Priority)
            {
                Logger.Debug($"{subject} keeps '{oldValue}' from higher-priority environment '{current.Environment}'; " +
                    $"ignoring '{newValue}' from '{origin.Environment}'");

                return false;
            }

            switch (onConflict)
            {
                case ConflictResolution.Last:
                    Logger.Warn($"{subject} overridden by environment '{origin.Environment}' ('{oldValue}' -> '{newValue}')");

                    return true;

                case ConflictResolution.First:
                    Logger.Warn($"{subject} keeps '{oldValue}' from environment '{current.Environment}'; " +
                        $"ignoring '{newValue}' from '{origin.Environment}'");

                    return false;

                default:
                    throw new ConfigurationValueException($"{subject} has conflicting values from environments " +
                        $"'{current.Environment}' ('{oldValue}') and '{origin.Environment}' ('{newValue}') with equal priority. " +
                        $"Set different priorities or change {EnvironmentParser.ONCONFLICT_ATTRIBUTE}.");
            }
        }

        /// <summary>Stamps the whole subtree with its origin (a clone carries no annotations).</summary>
        private static ConfigNode Annotate(ConfigNode root, MergeOrigin origin)
        {
            root.Origin = origin;

            foreach (var child in root.Children)
                Annotate(child, origin);

            return root;
        }

        private static void Reannotate(ConfigNode node, MergeOrigin origin) => node.Origin = origin;

        private static MergeOrigin OriginOf(ConfigNode node)
            => node.Origin as MergeOrigin ?? new MergeOrigin(0, "?");

        private static string Describe(ConfigNode node)
        {
            var name = node.Kind == ConfigNodeKind.Attribute ? $"attribute '{node.Name}'" : $"<{node.Name}>";

            return node.ItemName is string itemName ? $"{name} (name={itemName})" : name;
        }
    }
}
