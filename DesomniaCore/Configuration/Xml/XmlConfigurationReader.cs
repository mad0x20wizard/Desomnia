using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The one place that understands the XML representation of a configuration file. It
    /// reads the physical document into its abstract parts — the system directives (the
    /// <c>&lt;?system ...?&gt;</c> processing instructions outside the root element) and the
    /// content as a <see cref="ConfigNode"/> tree — so everything downstream (the environment
    /// pipeline, the providers, the merger) is independent of the lexical form.
    ///
    /// <para>XML-level rules applied here: namespace declaration attributes (xmlns) are
    /// structural and never become configuration nodes; all names are matched by their local
    /// name; a completely empty element (<c>&lt;x/&gt;</c>) yields an empty value, so its
    /// presence alone is bindable.</para>
    /// </summary>
    internal static partial class XmlConfigurationReader
    {
        /// <summary>Parses and wraps a stream — the content AS SERVED: for the real file the
        /// providers and the pipeline read through the source's file provider, where the
        /// migration layer (when composed underneath) already did its work.</summary>
        public static XmlConfigurationFile Read(Stream stream)
            => Read(Parse(() => XDocument.Load(stream, LoadOptions.PreserveWhitespace)));

        /// <summary>Parses and wraps a text (see <see cref="Read(Stream)"/>).</summary>
        public static XmlConfigurationFile Read(string text)
            => Read(Parse(text));

        /// <summary>
        /// The parse half: text to document, well-formedness errors reported as configuration
        /// errors. Whitespace is preserved on purpose - the migrator hands this very document to
        /// the modules and writes it back to the file, so it must keep the file's shape (the
        /// ConfigNode conversion ignores whitespace-only text anyway).
        /// </summary>
        public static XDocument Parse(string text)
            => Parse(() => XDocument.Parse(text, LoadOptions.PreserveWhitespace));

        private static XDocument Parse(Func<XDocument> load)
        {
            try
            {
                return load();
            }
            catch (XmlException ex)
            {
                throw new ConfigurationValueException($"The configuration is not well-formed XML: {ex.Message}", ex);
            }
        }

        /// <summary>The wrap half: root check and the system directives of an already parsed (and migrated) document.</summary>
        public static XmlConfigurationFile Read(XDocument document)
        {
            if (document.Root is not XElement root)
                throw new ConfigurationValueException("The configuration has no root element.");

            // the version declaration is validated on EVERY read (a contradictory or malformed
            // declaration fails fast, wherever the document enters), the check against the
            // loaded modules is the version check's (ModuleRegistry.Validate)
            return new XmlConfigurationFile(root, ReadSystemDirectives(document), XConfigVersion.Read(document));
        }

        #region System directives

        internal const string SYSTEM_DIRECTIVE_TARGET = "system";

        // exactly one key="value" pair (single or double quotes); the key may contain the
        // configuration path separator (e.g. "ProcessManager:pollInterval")
        [GeneratedRegex("""^\s*(?<key>[^\s='"]+)\s*=\s*("(?<value>[^"]*)"|'(?<value>[^']*)')\s*$""")]
        private static partial Regex DirectivePattern();

        private static List<SystemDirective> ReadSystemDirectives(XDocument document)
        {
            // a <?system?> below the root would suggest scoped semantics that do not exist -
            // the root configuration is process-wide, so it must stay outside the root
            if (document.Root!.DescendantNodes().OfType<XProcessingInstruction>()
                .FirstOrDefault(IsSystemDirective) is XProcessingInstruction misplaced)
            {
                throw new ConfigurationValueException($"<?{SYSTEM_DIRECTIVE_TARGET} {misplaced.Data}?> must be " +
                    $"placed outside of the <{document.Root.Name.LocalName}> root element.");
            }

            List<SystemDirective> directives = [];

            foreach (var instruction in document.Nodes().OfType<XProcessingInstruction>().Where(IsSystemDirective))
            {
                if (DirectivePattern().Match(instruction.Data) is not { Success: true } match)
                    throw new ConfigurationValueException($"Invalid processing instruction <?{SYSTEM_DIRECTIVE_TARGET} " +
                        $"{instruction.Data}?>; expected exactly one key=\"value\" pair.");

                directives.Add(new SystemDirective(match.Groups["key"].Value, match.Groups["value"].Value));
            }

            return directives;
        }

        private static bool IsSystemDirective(XProcessingInstruction instruction)
            => instruction.Target.Equals(SYSTEM_DIRECTIVE_TARGET, StringComparison.OrdinalIgnoreCase);

        #endregion

        #region ConfigNode conversion

        /// <summary>Converts an element (and its subtree) into the abstract representation.</summary>
        public static ConfigNode ToConfigNode(XElement element)
        {
            var node = new ConfigNode(element.Name.LocalName, ConfigNodeKind.Element);

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue; // structural, not configuration

                node.Children.Add(new ConfigNode(attribute.Name.LocalName, ConfigNodeKind.Attribute)
                {
                    Value = attribute.Value,
                });
            }

            foreach (var child in element.Elements())
                node.Children.Add(ToConfigNode(child));

            if (TextContent(element) is string text)
                node.Value = text;
            else if (node.Children.Count == 0)
                node.Value = string.Empty; // a bare element - its presence alone must bind

            return node;
        }

        /// <summary>The element's own text content (whitespace-only text does not count), or null.</summary>
        private static string? TextContent(XElement element)
        {
            var texts = element.Nodes().OfType<XText>().Where(text => !string.IsNullOrWhiteSpace(text.Value)).ToList();

            return texts.Count > 0 ? string.Concat(texts.Select(text => text.Value)) : null;
        }

        #endregion
    }

    /// <summary>One <c>&lt;?system key="value"?&gt;</c> processing instruction: a single entry
    /// of the root (process-lifetime) configuration, where <see cref="Key"/> is the full configuration path
    /// (e.g. "ProcessManager:pollInterval").</summary>
    internal sealed record SystemDirective(string Key, string Value);

    /// <summary>The abstract parts of one physical configuration file.</summary>
    internal sealed class XmlConfigurationFile(XElement root, IReadOnlyList<SystemDirective> systemDirectives, uint version)
    {
        /// <summary>The root element's local name (which decides passthrough vs. augmenting mode).</summary>
        public string RootName => root.Name.LocalName;

        /// <summary>The root element - for the environment parser, which needs the XML level
        /// (namespaced condition attributes) before block contents become ConfigNodes.</summary>
        public XElement Root => root;

        /// <summary>The configuration format version the file declares (see <see cref="XConfigVersion"/>) -
        /// checked against the loaded modules by <c>ModuleRegistry.Validate</c>.</summary>
        public uint Version => version;

        /// <summary>The <c>&lt;?system?&gt;</c> directives, in document order.</summary>
        public IReadOnlyList<SystemDirective> SystemDirectives => systemDirectives;

        /// <summary>The whole content as abstract configuration.</summary>
        public ConfigNode ToConfigNode()
        {
            var node = XmlConfigurationReader.ToConfigNode(root);

            // the root's version attribute declares the file format (see XConfigVersion);
            // it is not configuration data
            node.Children.RemoveAll(child => child.Kind == ConfigNodeKind.Attribute
                && child.Name.Equals(XConfigVersion.VERSION_ATTRIBUTE, StringComparison.OrdinalIgnoreCase));

            return node;
        }
    }
}
