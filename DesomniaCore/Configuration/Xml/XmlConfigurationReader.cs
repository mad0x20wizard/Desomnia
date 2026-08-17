using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The one place that understands the XML representation of a configuration file. It
    /// reads the physical document into its abstract parts — the global directives (the
    /// <c>&lt;?global ...?&gt;</c> processing instructions outside the root element) and the
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
        public static XmlConfigurationFile Read(Stream stream)
            => Read(() => XDocument.Load(stream));

        public static XmlConfigurationFile Read(string text)
            => Read(() => XDocument.Parse(text));

        private static XmlConfigurationFile Read(Func<XDocument> load)
        {
            XDocument document;
            try
            {
                document = load();
            }
            catch (XmlException ex)
            {
                throw new ConfigurationValueException($"The configuration is not well-formed XML: {ex.Message}", ex);
            }

            if (document.Root is not XElement root)
                throw new ConfigurationValueException("The configuration has no root element.");

            return new XmlConfigurationFile(root, ReadGlobalDirectives(document));
        }

        #region Global directives

        internal const string GLOBAL_DIRECTIVE_TARGET = "global";

        // exactly one key="value" pair (single or double quotes); the key may contain the
        // configuration path separator (e.g. "ProcessManager:pollInterval")
        [GeneratedRegex("""^\s*(?<key>[^\s='"]+)\s*=\s*("(?<value>[^"]*)"|'(?<value>[^']*)')\s*$""")]
        private static partial Regex DirectivePattern();

        private static List<GlobalDirective> ReadGlobalDirectives(XDocument document)
        {
            // a <?global?> below the root would suggest scoped semantics that do not exist -
            // the root configuration is process-wide, so it must stay outside the root
            if (document.Root!.DescendantNodes().OfType<XProcessingInstruction>()
                .FirstOrDefault(IsGlobalDirective) is XProcessingInstruction misplaced)
            {
                throw new ConfigurationValueException($"<?{GLOBAL_DIRECTIVE_TARGET} {misplaced.Data}?> must be " +
                    $"placed outside of the <{document.Root.Name.LocalName}> root element.");
            }

            List<GlobalDirective> directives = [];

            foreach (var instruction in document.Nodes().OfType<XProcessingInstruction>().Where(IsGlobalDirective))
            {
                if (DirectivePattern().Match(instruction.Data) is not { Success: true } match)
                    throw new ConfigurationValueException($"Invalid processing instruction <?{GLOBAL_DIRECTIVE_TARGET} " +
                        $"{instruction.Data}?>; expected exactly one key=\"value\" pair.");

                directives.Add(new GlobalDirective(match.Groups["key"].Value, match.Groups["value"].Value));
            }

            return directives;
        }

        private static bool IsGlobalDirective(XProcessingInstruction instruction)
            => instruction.Target.Equals(GLOBAL_DIRECTIVE_TARGET, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>One <c>&lt;?global key="value"?&gt;</c> processing instruction: a single entry
    /// of the root (process-lifetime) configuration, where <see cref="Key"/> is the full configuration path
    /// (e.g. "ProcessManager:pollInterval").</summary>
    internal sealed record GlobalDirective(string Key, string Value);

    /// <summary>The abstract parts of one physical configuration file.</summary>
    internal sealed class XmlConfigurationFile(XElement root, IReadOnlyList<GlobalDirective> globalDirectives)
    {
        /// <summary>The root element's local name (which decides passthrough vs. augmenting mode).</summary>
        public string RootName => root.Name.LocalName;

        /// <summary>The root element - for the environment parser, which needs the XML level
        /// (namespaced condition attributes) before block contents become ConfigNodes.</summary>
        public XElement Root => root;

        /// <summary>The <c>&lt;?global?&gt;</c> directives, in document order.</summary>
        public IReadOnlyList<GlobalDirective> GlobalDirectives => globalDirectives;

        /// <summary>The whole content as abstract configuration.</summary>
        public ConfigNode ToConfigNode() => XmlConfigurationReader.ToConfigNode(root);
    }
}
