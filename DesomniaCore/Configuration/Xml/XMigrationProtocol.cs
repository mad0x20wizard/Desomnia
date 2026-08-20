using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The migration protocol a migrated file carries: one comment per migration run (dated),
    /// right below the &lt;?config?&gt; header — after the protocols of earlier runs, before
    /// any &lt;?system?&gt; directive — listing every step the run wrote — its notes as
    /// bullet lines, or a bare "Version 2 -> 3." for a step without any:
    /// <code>
    /// &lt;!-- MIGRATION PROTOCOL (19.08.2026)
    ///   Version 1 -> 2:
    ///     - /SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort -> renamed to @watchPort
    ///   Version 2 -> 3.
    /// --&gt;
    /// </code>
    /// A run extends its own comment step by step (the file is written after every step);
    /// a later run adds a new comment after it, so the protocols read chronologically (as
    /// long as the user leaves them where they are; a protocol found elsewhere is not counted).
    /// </summary>
    internal sealed class XMigrationProtocol
    {
        internal const string TITLE = "MIGRATION PROTOCOL";

        internal const string DATE_FORMAT = "dd.MM.yyyy";

        XComment? _comment;

        /// <summary>Records the step in this run's protocol comment (created below the header on the first step).</summary>
        public void Record(XDocument document, uint sourceVersion, uint targetVersion, IReadOnlyList<MigrationAnnotation> notes)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(notes);

            StringBuilder text = new(_comment?.Value ?? $" {TITLE} ({DateTime.Now.ToString(DATE_FORMAT, CultureInfo.InvariantCulture)})\n");

            text.Append($"  Version {sourceVersion} -> {targetVersion}");

            var listed = notes.Where(note => note.Level != LogLevel.None).ToList();

            if (listed.Count == 0)
            {
                text.Append(".\n");
            }
            else
            {
                text.Append(":\n");

                foreach (var note in listed)
                    text.Append("    - ").Append(note.Path).Append(" -> ").Append(note.Message).Append('\n');
            }

            if (_comment is null)
            {
                _comment = new XComment(Sanitize(text.ToString()));

                Insert(document, _comment);
            }
            else
            {
                _comment.Value = Sanitize(text.ToString());
            }
        }

        /// <summary>Whether the comment is a migration protocol (of this or an earlier run).</summary>
        public static bool Is(XComment comment)
            => comment.Value.TrimStart().StartsWith(TITLE, StringComparison.Ordinal);

        // below the header, after the protocols already there (whitespace between them does not
        // count), each on its own line; a document without a header (cannot happen on the
        // migrator's write path - a persistent migration is declared in the header - but the
        // protocol does not rely on that) gets the protocol in front of the root
        private static void Insert(XDocument document, XComment comment)
        {
            XNode? anchor = document.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(XConfigDirective.Is);

            if (anchor is null)
            {
                (document.Root ?? throw new ArgumentException("The document has no root element.", nameof(document)))
                    .AddBeforeSelf(comment, new XText("\n"));

                return;
            }

            for (var node = anchor.NextNode; node is not null; node = node.NextNode)
            {
                if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
                    continue;

                if (node is XComment earlier && Is(earlier))
                {
                    anchor = earlier;
                    continue;
                }

                break;
            }

            if (anchor.NextNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
                whitespace.AddAfterSelf(comment, new XText("\n"));
            else
                anchor.AddAfterSelf(new XText("\n"), comment, new XText("\n"));
        }

        // an XML comment may neither contain "--" nor end with "-"
        private static string Sanitize(string text)
        {
            while (text.Contains("--"))
                text = text.Replace("--", "- -");

            if (text.EndsWith('-'))
                text += " ";

            return text;
        }
    }
}
