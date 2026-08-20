using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Migration;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The optional <c>&lt;?config version="..." autoMigrate="..." writeBackupXML="..."?&gt;</c>
    /// header of a configuration file — what the file says about the handling of its own format:
    /// whether an outdated file may be migrated and whether the file is written then. Most files
    /// need no header at all (migration defaults to "transient", and the version lives on the
    /// root element, see <see cref="XConfigVersion"/>); it is only written to FORBID the
    /// migration, make it persistent, or carry the version in header style. Like the XML
    /// declaration it describes the file rather than the configuration, so it is the first
    /// processing instruction of the file (right after <c>&lt;?xml?&gt;</c>, before any
    /// <c>&lt;?system?&gt;</c> directive and before the root element), at most once, read here
    /// off the raw document before anything else looks at it — it is not configuration data (no
    /// provider serves it) and not reloadable.
    /// </summary>
    internal static partial class XConfigDirective
    {
        internal const string TARGET = "config";

        internal const string VERSION_KEY = "version";

        static readonly string[] KNOWN_KEYS = [VERSION_KEY, MigrationSettings.AUTO_MIGRATE_KEY, MigrationSettings.WRITE_BACKUP_KEY];

        // pseudo-attributes: key="value" or key='value' pairs separated by whitespace, nothing else
        [GeneratedRegex("""^(?:\s*(?<key>[A-Za-z_][\w.-]*)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'))*\s*$""", RegexOptions.CultureInvariant)]
        private static partial Regex HeaderPattern();

        /// <summary>
        /// Reads the header, or null when the file has none. A header below the root, after
        /// another processing instruction or after the root, a second one, a malformed one or an
        /// unknown key is a <see cref="ConfigurationValueException"/>. Every setting of the
        /// header itself is optional.
        /// </summary>
        public static XConfigHeader? Read(XDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            if (document.Root?.DescendantNodes().OfType<XProcessingInstruction>().FirstOrDefault(Is) is XProcessingInstruction misplaced)
                throw new ConfigurationValueException($"<?{TARGET} {misplaced.Data}?> must be placed outside of the " +
                    $"<{document.Root.Name.LocalName}> root element.");

            var nodes = document.Nodes().ToList();

            int index = nodes.FindIndex(node => node is XProcessingInstruction instruction && Is(instruction));

            if (index < 0)
                return null;

            if (nodes.Skip(index + 1).Any(node => node is XProcessingInstruction instruction && Is(instruction)))
                throw new ConfigurationValueException($"Only one <?{TARGET}?> header is allowed.");

            // the header of the whole file: nothing but the XML declaration (and comments) before it
            if (nodes.Take(index).Any(node => node is XProcessingInstruction or XElement))
                throw new ConfigurationValueException($"<?{TARGET}?> must be the first processing instruction of the " +
                    "configuration file, right after the XML declaration.");

            var instruction = (XProcessingInstruction)nodes[index];

            var values = Parse(instruction);

            uint? version = values.TryGetValue(VERSION_KEY, out string? declared) ? ParseVersion(declared) : null;

            return new XConfigHeader(instruction, version,
                values.GetValueOrDefault(MigrationSettings.AUTO_MIGRATE_KEY),
                values.GetValueOrDefault(MigrationSettings.WRITE_BACKUP_KEY));
        }

        /// <summary>Whether the instruction is the header (the target is matched case-insensitively, like every name in the configuration).</summary>
        public static bool Is(XProcessingInstruction instruction)
            => instruction.Target.Equals(TARGET, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Stamps the version into an existing header: only the value changes, everything else
        /// in the instruction (the other settings, spacing, quotes) stays as the user wrote it.
        /// Only called for a header that declares a version — one that does not keeps its style
        /// (the root element's attribute is the authoritative declaration anyway).
        /// </summary>
        public static void SetVersion(XProcessingInstruction instruction, uint version)
        {
            ArgumentNullException.ThrowIfNull(instruction);

            string stamp = version.ToString(CultureInfo.InvariantCulture);

            var match = HeaderPattern().Match(instruction.Data);

            var keys = match.Groups["key"].Captures;
            var values = match.Groups["value"].Captures;

            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Value.Equals(VERSION_KEY, StringComparison.OrdinalIgnoreCase))
                {
                    instruction.Data = string.Concat(instruction.Data.AsSpan(0, values[i].Index), stamp, instruction.Data.AsSpan(values[i].Index + values[i].Length));
                    return;
                }
            }

            // no version in the header: lead with it (defensive - see the summary)
            instruction.Data = $"{VERSION_KEY}=\"{stamp}\" {instruction.Data.TrimStart()}";
        }

        internal static uint ParseVersion(string value)
        {
            if (!uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint version) || version == 0)
                throw new ConfigurationValueException($"Invalid <?{TARGET} {VERSION_KEY}=\"{value}\"?>; expected a positive integer.");

            return version;
        }

        private static Dictionary<string, string> Parse(XProcessingInstruction instruction)
        {
            if (HeaderPattern().Match(instruction.Data) is not { Success: true } match)
                throw new ConfigurationValueException($"Invalid processing instruction <?{TARGET} {instruction.Data}?>; " +
                    $"expected key=\"value\" pairs ({string.Join(", ", KNOWN_KEYS)}).");

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

            var keys = match.Groups["key"].Captures;
            var valuesFound = match.Groups["value"].Captures;

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i].Value;

                if (!KNOWN_KEYS.Any(known => known.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    throw new ConfigurationValueException($"Unknown <?{TARGET}?> setting '{key}'; " +
                        $"expected {string.Join(", ", KNOWN_KEYS)}.");

                if (!values.TryAdd(key, valuesFound[i].Value))
                    throw new ConfigurationValueException($"Duplicate <?{TARGET}?> setting '{key}'.");
            }

            return values;
        }
    }

    /// <summary>
    /// One parsed <c>&lt;?config?&gt;</c> header: the instruction itself (a version stamp goes
    /// back into it), the format version IF the header declares one and the migration settings
    /// as written (null = not declared).
    /// </summary>
    internal sealed record XConfigHeader(XProcessingInstruction Instruction, uint? Version, string? AutoMigrate, string? WriteBackup);

    /// <summary>
    /// The configuration format version a file declares — since format version 1 (and again,
    /// after an interlude as a header setting) as a <c>version="..."</c> attribute on the ROOT
    /// element, the authoritative declaration. The <c>&lt;?config?&gt;</c> header MAY carry the
    /// version too (header style); when both places declare it, they must match. A file that
    /// declares no version at all is in version 1.
    /// </summary>
    internal static class XConfigVersion
    {
        /// <summary>The root element's version attribute - the authoritative declaration.</summary>
        internal const string VERSION_ATTRIBUTE = "version";

        /// <summary>Reads the file's declared version (see the class summary). A bad or
        /// contradictory declaration is a <see cref="ConfigurationValueException"/>.</summary>
        public static uint Read(XDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            return Read(document, XConfigDirective.Read(document));
        }

        /// <summary>The overload for a caller that has already read the header (the migration document).</summary>
        internal static uint Read(XDocument document, XConfigHeader? header)
        {
            uint? declared = null;

            if (document.Root?.AttributeNamed(VERSION_ATTRIBUTE) is XAttribute attribute)
            {
                if (!uint.TryParse(attribute.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint version) || version == 0)
                    throw new ConfigurationValueException($"Invalid <{document.Root!.Name.LocalName} " +
                        $"{attribute.Name.LocalName}=\"{attribute.Value}\">; expected a positive integer.");

                declared = version;
            }

            if (declared is uint fromRoot && header?.Version is uint fromHeader && fromRoot != fromHeader)
                throw new ConfigurationValueException($"<{document.Root!.Name.LocalName}> declares " +
                    $"{VERSION_ATTRIBUTE}=\"{declared}\" while <?{XConfigDirective.TARGET} {header.Instruction.Data}?> " +
                    $"declares version {fromHeader}; the two declarations must match.");

            return declared ?? header?.Version ?? 1;
        }
    }
}
