using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The migration helpers of an XML configuration document (see <see cref="IXConfigurationMigration"/>):
    /// each one records a note about the node — with the node's path as it is BEFORE the
    /// change — and then carries out the change, so a migration written with them is fully
    /// reported and produces no warnings of its own. Every note ends up in the log as
    /// <c>{XPath} -> '{message}'</c> and, when the file is rewritten, in the comment header of
    /// the migrated file. A change made past the helpers is still reported: the migration
    /// tracks the document and records such a change as a Warning ("outside a migration helper").
    ///
    /// <para>Rules: a note above Warning aborts the startup, so the user must fix the file by
    /// hand; <see cref="LogLevel.None"/> is accepted for a deliberately silent change (stored,
    /// never logged). A helper used inside another helper's action is a detail of that
    /// operation and takes no note of its own — unless it is given an explicit reason.</para>
    /// </summary>
    public static class XMigrationExtensions
    {
        #region The general seam

        /// <summary>
        /// Records the note (<paramref name="reason"/>, at <paramref name="level"/>, with the
        /// node's current path) and then runs <paramref name="action"/> on the node with the
        /// change tracking suspended — for any change the specific helpers below do not cover.
        /// The specific helpers are built on this.
        /// </summary>
        /// <exception cref="InvalidOperationException">The node is not part of a document.</exception>
        public static void Migrate<T>(this T node, LogLevel level, string reason, Action<T> action) where T : XObject
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(reason);
            ArgumentNullException.ThrowIfNull(action);

            var tracker = XMigrationTracker.For(node);

            // an explicit reason is always recorded, nested or not
            tracker.Note(XPathDescriber.Describe(node), level, reason);

            using (tracker.Enter())
                action(node);
        }

        /// <summary>
        /// Records a note about the node WITHOUT changing anything: an Error for a change that
        /// cannot be made automatically (the startup is aborted), a Warning to draw attention to
        /// something the user should look at.
        /// </summary>
        /// <exception cref="InvalidOperationException">The node is not part of a document.</exception>
        public static void AnnotateMigration(this XObject node, LogLevel level, string message)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(message);

            var tracker = XMigrationTracker.For(node);

            tracker.Note(XPathDescriber.Describe(node), level, message);
        }

        #endregion

        #region Rename

        /// <summary>
        /// Renames the attribute (its namespace stays). An <see cref="XAttribute"/> cannot change
        /// its name, so it is replaced — in place, keeping its position among its siblings — and
        /// the replacement is returned: the original is detached from here on.
        /// </summary>
        public static XAttribute MigrateRename(this XAttribute attribute, string name, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(attribute);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var element = attribute.Parent ?? throw new InvalidOperationException("The attribute is not attached to an element.");

            var renamed = new XAttribute(attribute.Name.Namespace + name, attribute.Value);

            Perform(attribute, level, $"renamed to @{name}", reason, _ =>
            {
                var attributes = element.Attributes().Select(sibling => sibling == attribute ? renamed : sibling).ToList();

                element.ReplaceAttributes(attributes);
            });

            return renamed;
        }

        /// <summary>Renames the element (its namespace stays).</summary>
        public static void MigrateRename(this XElement element, string name, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Perform(element, level, $"renamed to <{name}>", reason, e => e.Name = e.Name.Namespace + name);
        }

        #endregion

        #region Value

        /// <summary>Sets the attribute's value.</summary>
        public static void MigrateSetValue(this XAttribute attribute, string value, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(attribute);
            ArgumentNullException.ThrowIfNull(value);

            Perform(attribute, level, $"changed from \"{attribute.Value}\" to \"{value}\"", reason, a => a.Value = value);
        }

        /// <summary>Sets the element's text content — replacing ALL of its content, child elements included, as <see cref="XElement.Value"/> does.</summary>
        public static void MigrateSetValue(this XElement element, string value, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(value);

            Perform(element, level, $"content changed to \"{value}\"", reason, e => e.Value = value);
        }

        #endregion

        #region Remove

        /// <summary>Removes the attribute. Warning by default: a setting the user wrote disappears.</summary>
        public static void MigrateRemove(this XAttribute attribute, LogLevel level = LogLevel.Warning, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(attribute);

            Perform(attribute, level, "removed", reason, a => a.Remove());
        }

        /// <summary>Removes the node (an element with its whole subtree). Warning by default: content the user wrote disappears.</summary>
        public static void MigrateRemove(this XNode node, LogLevel level = LogLevel.Warning, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(node);

            Perform(node, level, "removed", reason, n => n.Remove());
        }

        #endregion

        #region Add

        /// <summary>Adds a new attribute to the element (the note names the attribute's path).</summary>
        public static void MigrateAdd(this XElement element, XAttribute attribute, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(attribute);

            if (attribute.Parent is not null)
                throw new ArgumentException("The attribute is attached to an element already; add a new attribute (or move it with MigrateMove).", nameof(attribute));

            PerformAdd(element, attribute, level, $"added @{attribute.Name.LocalName} = \"{attribute.Value}\"", reason, () => element.Add(attribute));
        }

        /// <summary>Adds a new child node to the container, last (the note names the node's path).</summary>
        public static void MigrateAdd(this XContainer container, XNode node, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Parent is not null || node.Document is not null) // attached somewhere (a root element has no parent, but a document)
                throw new ArgumentException("The node is part of a document already; add a new node (or move it with MigrateMove).", nameof(node));

            PerformAdd(container, node, level, $"added {Describe(node)}", reason, () => container.Add(node));
        }

        #endregion

        #region Replace and move

        /// <summary>Replaces the node with another (new) node.</summary>
        public static void MigrateReplace(this XNode node, XNode replacement, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(replacement);

            Perform(node, level, $"replaced by {Describe(replacement)}", reason, n => n.ReplaceWith(replacement));
        }

        /// <summary>Moves the attribute to another element (same name, same value; the note names the target's path).</summary>
        public static void MigrateMove(this XAttribute attribute, XElement target, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(attribute);
            ArgumentNullException.ThrowIfNull(target);

            Perform(attribute, level, $"moved to {XPathDescriber.Describe(target)}", reason, a =>
            {
                a.Remove();

                target.Add(a); // detached now, so the same object is attached again (not a copy)
            });
        }

        /// <summary>Moves the node (with its subtree) into another container, last.</summary>
        public static void MigrateMove(this XNode node, XContainer target, LogLevel level = LogLevel.Information, string? reason = null)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(target);

            Perform(node, level, $"moved to {XPathDescriber.Describe(target)}", reason, n =>
            {
                n.Remove();

                target.Add(n);
            });
        }

        #endregion

        #region Internals

        // annotate first (the path as the user's file has it), then change with the tracking
        // suspended; an implicit (default-message) note is only taken at the top level - a
        // helper used inside another helper's action is a detail of that operation
        private static void Perform<T>(T node, LogLevel level, string description, string? reason, Action<T> action) where T : XObject
        {
            var tracker = XMigrationTracker.For(node);

            if (reason is not null || tracker.Depth == 0)
                tracker.Note(XPathDescriber.Describe(node), level, Compose(description, reason));

            using (tracker.Enter())
                action(node);
        }

        // an added node has no path before it is added: change first (suspended), then note the
        // path it got - the same top-level-or-explicit rule applies
        private static void PerformAdd(XObject parent, XObject added, LogLevel level, string description, string? reason, Action add)
        {
            var tracker = XMigrationTracker.For(parent);

            using (tracker.Enter())
            {
                add();

                if (reason is not null || tracker.Depth == 1)
                    tracker.Note(XPathDescriber.Describe(added), level, Compose(description, reason));
            }
        }

        private static string Compose(string description, string? reason)
            => reason is null ? description : $"{description} - {reason}";

        private static string Describe(XNode node) => node switch
        {
            XElement element => $"<{element.Name.LocalName}>",
            XComment => "a comment",
            XProcessingInstruction instruction => $"<?{instruction.Target}?>",
            XText => "text",
            _ => node.NodeType.ToString(),
        };

        /// <summary>The notes taken on the document so far (helpers and tracked changes), emptying the store. Test seam.</summary>
        internal static IReadOnlyList<MigrationAnnotation> TakeMigrationAnnotations(this XDocument document)
            => XMigrationTracker.Of(document)?.Take() ?? [];

        #endregion
    }
}
