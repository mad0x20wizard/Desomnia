using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The migration bookkeeping of one <see cref="XDocument"/>, attached to it as an
    /// annotation: the notes taken so far, and — while a migration step runs — the change
    /// tracking that makes sure no change escapes: XLinq raises <c>Changing</c>/<c>Changed</c>
    /// on an object and all its ancestors, so ONE subscription on the document sees every
    /// change in the tree, and any change made outside a migration helper (see
    /// <see cref="XMigrationExtensions"/>) is recorded as a Warning. A helper suppresses that
    /// for the duration of its own action (<see cref="Enter"/>) — it has already recorded the
    /// intent, with the path as it was BEFORE the change.
    /// </summary>
    internal sealed class XMigrationTracker
    {
        readonly XDocument _document;

        readonly List<MigrationAnnotation> _notes = [];

        // > 0 while a helper's action runs (nested helpers deepen it): tracked changes are not
        // annotated, and a nested helper takes no implicit note of its own
        int _depth;

        bool _listening;

        // captured at Changing (the node is still attached and unchanged then), consumed at
        // Changed - XLinq raises the two back to back, on the same thread
        (string Path, string? Old)? _pending;

        private XMigrationTracker(XDocument document)
        {
            _document = document;
        }

        /// <summary>The tracker of the node's document, created on first use (a helper used
        /// outside a migration run still records its notes; only a run listens for changes).</summary>
        /// <exception cref="InvalidOperationException">The node is not part of a document.</exception>
        public static XMigrationTracker For(XObject node)
        {
            ArgumentNullException.ThrowIfNull(node);

            if (node.Document is not XDocument document)
                throw new InvalidOperationException("The node is not part of a document; migrate nodes while they are attached to the document.");

            return For(document);
        }

        /// <summary>The tracker of the document, created on first use.</summary>
        public static XMigrationTracker For(XDocument document)
        {
            if (document.Annotation<XMigrationTracker>() is not XMigrationTracker tracker)
                document.AddAnnotation(tracker = new XMigrationTracker(document));

            return tracker;
        }

        /// <summary>The tracker of the document, if it has one (without creating it).</summary>
        public static XMigrationTracker? Of(XDocument document) => document.Annotation<XMigrationTracker>();

        /// <summary>How many helper actions are running right now (0 = top level).</summary>
        public int Depth => _depth;

        #region Notes

        public void Note(string path, LogLevel level, string message) => _notes.Add(new MigrationAnnotation(path, level, message));

        /// <summary>The notes taken since the last call, in order; empties the store.</summary>
        public IReadOnlyList<MigrationAnnotation> Take()
        {
            var taken = _notes.ToArray();

            _notes.Clear();

            return taken;
        }

        #endregion

        #region Helper scopes

        /// <summary>Marks the start of a helper's action: tracked changes are not annotated until
        /// the returned scope is disposed, and nested helpers take no implicit notes.</summary>
        public IDisposable Enter()
        {
            _depth++;

            return new Scope(this);
        }

        private sealed class Scope(XMigrationTracker tracker) : IDisposable
        {
            bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;

                tracker._depth--;
            }
        }

        #endregion

        #region Change tracking

        /// <summary>Starts recording changes made outside a helper (idempotent).</summary>
        public void Listen()
        {
            if (_listening)
                return;

            _document.Changing += OnChanging;
            _document.Changed += OnChanged;

            _listening = true;
        }

        /// <summary>Stops recording changes (idempotent).</summary>
        public void Unlisten()
        {
            if (!_listening)
                return;

            _document.Changing -= OnChanging;
            _document.Changed -= OnChanged;

            _listening = false;
        }

        /// <summary>Ends the bookkeeping: stops listening and detaches the tracker from the
        /// document, which leaves the migration without a live subscription or stale notes.</summary>
        public void Release()
        {
            Unlisten();

            _document.RemoveAnnotations<XMigrationTracker>();
        }

        private void OnChanging(object? sender, XObjectChangeEventArgs e)
        {
            if (_depth > 0 || sender is not XObject node)
                return;

            // a node being added is not attached yet - its path exists only after the change;
            // for everything else this is the last moment the old state can be seen
            _pending = e.ObjectChange == XObjectChange.Add ? null : (XPathDescriber.Describe(node), ValueOf(node));
        }

        private void OnChanged(object? sender, XObjectChangeEventArgs e)
        {
            if (_depth > 0 || sender is not XObject node)
                return;

            var pending = _pending;

            _pending = null;

            const string suffix = "outside a migration helper";

            switch (e.ObjectChange)
            {
                case XObjectChange.Add:
                    Note(XPathDescriber.Describe(node), LogLevel.Warning, $"added {suffix}");
                    break;

                case XObjectChange.Remove:
                    Note(pending?.Path ?? XPathDescriber.Describe(node), LogLevel.Warning, $"removed {suffix}");
                    break;

                case XObjectChange.Name:
                    Note(pending?.Path ?? XPathDescriber.Describe(node), LogLevel.Warning, $"renamed to {NameOf(node)} {suffix}");
                    break;

                case XObjectChange.Value:
                    Note(pending?.Path ?? XPathDescriber.Describe(node), LogLevel.Warning,
                        $"changed from \"{pending?.Old}\" to \"{ValueOf(node)}\" {suffix}");
                    break;
            }
        }

        private static string? ValueOf(XObject node) => node switch
        {
            XAttribute attribute => attribute.Value,
            XText text => text.Value,
            XComment comment => comment.Value,
            XProcessingInstruction instruction => instruction.Data,
            XElement element => element.Value,
            _ => null,
        };

        private static string NameOf(XObject node) => node switch
        {
            XElement element => $"<{element.Name.LocalName}>",
            XProcessingInstruction instruction => instruction.Target,
            XDocumentType type => type.Name,
            _ => node.NodeType.ToString(),
        };

        #endregion
    }
}
