using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using NLog;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Parses and validates an &lt;EnvironmentMonitor&gt; configuration document into
    /// its <see cref="EnvironmentBlock"/>s. All structural errors are reported as
    /// <see cref="ConfigurationValueException"/>, aborting startup.
    /// </summary>
    internal static class EnvironmentParser
    {
        static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal const string ROOT_ELEMENT = "EnvironmentMonitor";
        internal const string ENVIRONMENT_ELEMENT = "Environment";
        internal const string DEFAULT_ENVIRONMENT_ELEMENT = "DefaultEnvironment";
        internal const string SYSTEM_MONITOR_ELEMENT = "SystemMonitor";

        internal const string NAME_ATTRIBUTE = "name";
        internal const string DEBOUNCE_ATTRIBUTE = "debounce";
        internal const string WRITE_EFFECTIVE_XML_ATTRIBUTE = "writeEffectiveXML";
        internal const string WRITE_EFFECTIVE_CONFIGURATION_ATTRIBUTE = "writeEffectiveConfiguration";
        internal const string ONCONFLICT_ATTRIBUTE = "onConflict";
        internal const string ONLY_IF_ATTRIBUTE = "onlyIf";
        internal const string ONLY_IF_NOT_ATTRIBUTE = "onlyIfNot";
        internal const string PRIORITY_ATTRIBUTE = "priority";

        internal static readonly TimeSpan DEFAULT_DEBOUNCE = TimeSpan.FromSeconds(3);

        /// <summary>The <see cref="ONLY_IF_ATTRIBUTE"/> merge-mode keywords (mirrors the switch in <see cref="ParseOnlyIf"/>).
        /// An environment must not use one as its name, since onlyIf treats every non-keyword value as an environment reference.</summary>
        static readonly string[] RESERVED_NAMES = ["always", "never", "else"];

        /// <summary>The parsed root attributes and environment blocks of an &lt;EnvironmentMonitor&gt; document.</summary>
        internal sealed record Result(TimeSpan Debounce, string? WriteEffectiveXML,
            string? WriteEffectiveConfiguration, ConflictResolution OnConflict, IReadOnlyList<EnvironmentBlock> Blocks);

        public static Result Parse(XDocument document)
        {
            XElement root = document.Root!; // root name already verified by the caller

            if (root.Descendants().Any(element => Is(element, ROOT_ELEMENT)))
                throw new ConfigurationValueException($"<{ROOT_ELEMENT}> must not be nested.");

            (TimeSpan debounce, var outputs, ConflictResolution onConflict) = ParseRootAttributes(root);

            List<EnvironmentBlock> blocks = [];

            bool hasDefault = false;
            uint anonymous = 0;

            foreach (var child in root.Elements())
            {
                if (Is(child, ENVIRONMENT_ELEMENT))
                {
                    blocks.Add(ParseEnvironment(child, ref anonymous));
                }
                else if (Is(child, DEFAULT_ENVIRONMENT_ELEMENT))
                {
                    if (hasDefault)
                        throw new ConfigurationValueException($"Only one <{DEFAULT_ENVIRONMENT_ELEMENT}> is allowed.");

                    hasDefault = true;

                    blocks.Add(ParseDefaultEnvironment(child));
                }
                else
                {
                    throw new ConfigurationValueException($"Unknown element <{child.Name.LocalName}> below <{ROOT_ELEMENT}>; " +
                        $"expected <{ENVIRONMENT_ELEMENT}> or <{DEFAULT_ENVIRONMENT_ELEMENT}>.");
                }
            }

            if (blocks.Count == 0)
                throw new ConfigurationValueException($"<{ROOT_ELEMENT}> defines no environments.");

            ValidateReferences(blocks);

            return new Result(debounce, outputs.EffectiveXML, outputs.EffectiveConfiguration, onConflict, blocks);
        }

        private static (TimeSpan Debounce, (string? EffectiveXML, string? EffectiveConfiguration) Outputs, ConflictResolution OnConflict) ParseRootAttributes(XElement root)
        {
            TimeSpan debounce = DEFAULT_DEBOUNCE;
            string? writeEffectiveXML = null;
            string? writeEffectiveConfiguration = null;
            ConflictResolution onConflict = ConflictResolution.Last;

            foreach (var attribute in root.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue; // structural: declares a condition namespace (e.g. xmlns:env)

                var name = attribute.Name.LocalName;

                if (name.Equals(DEBOUNCE_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    var normalized = ValueVariations.NormalizeTimeSpan(attribute.Value);

                    if (!TimeSpan.TryParse(normalized, out debounce) || debounce < TimeSpan.Zero)
                        throw new ConfigurationValueException($"Invalid {DEBOUNCE_ATTRIBUTE} = {attribute.Value}");
                }
                else if (name.Equals(WRITE_EFFECTIVE_XML_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(attribute.Value))
                        writeEffectiveXML = attribute.Value;
                }
                else if (name.Equals(WRITE_EFFECTIVE_CONFIGURATION_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(attribute.Value))
                        writeEffectiveConfiguration = attribute.Value;
                }
                else if (name.Equals(XConfigVersion.VERSION_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    // the file's format declaration (see XConfigVersion) - validated by the
                    // reader and checked by the version check, not this parser's business
                }
                else if (name.Equals(ONCONFLICT_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    onConflict = attribute.Value.ToLowerInvariant() switch
                    {
                        "last" => ConflictResolution.Last,
                        "first" => ConflictResolution.First,
                        "error" => ConflictResolution.Error,

                        _ => throw new ConfigurationValueException($"Invalid {ONCONFLICT_ATTRIBUTE} = \"{attribute.Value}\"; " +
                            $"expected \"last\", \"first\" or \"error\"."),
                    };
                }
                else
                {
                    Logger.Warn($"<{ROOT_ELEMENT}>: ignoring unknown attribute '{name}'.");
                }
            }

            return (debounce, (writeEffectiveXML, writeEffectiveConfiguration), onConflict);
        }

        private static EnvironmentBlock ParseDefaultEnvironment(XElement element)
        {
            var mode = EnvironmentMergeMode.Always;
            int priority = 0;
            string? onlyIf = null;
            string? onlyIfNot = null;

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue; // structural: declares a condition namespace (e.g. xmlns:env)

                if (attribute.Name.NamespaceName.Length > 0)
                {
                    // a namespaced attribute is always a condition - which the default must not have
                    throw new ConfigurationValueException($"<{DEFAULT_ENVIRONMENT_ELEMENT}> must not have " +
                        $"a '{DescribeAttribute(element, attribute)}' attribute.");
                }

                if (attribute.Name.LocalName.Equals(ONLY_IF_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    (mode, onlyIf) = ParseOnlyIf(attribute.Value, DEFAULT_ENVIRONMENT_ELEMENT, allowElse: true);
                }
                else if (attribute.Name.LocalName.Equals(ONLY_IF_NOT_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    onlyIfNot = ParseOnlyIfNot(attribute.Value);
                }
                else if (attribute.Name.LocalName.Equals(PRIORITY_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    priority = ParsePriority(attribute.Value);
                }
                else
                {
                    throw new ConfigurationValueException($"<{DEFAULT_ENVIRONMENT_ELEMENT}> must not have " +
                        $"a '{attribute.Name.LocalName}' attribute.");
                }
            }

            return new EnvironmentBlock
            {
                DisplayName = "default",
                IsDefault = true,
                MergeMode = mode,
                Priority = priority,
                OnlyIf = onlyIf,
                OnlyIfNot = onlyIfNot,
                ConditionAttributes = [],
                Content = NormalizeContent(element, "default"),
            };
        }

        private static EnvironmentBlock ParseEnvironment(XElement element, ref uint anonymous)
        {
            string? name = null;
            var mode = EnvironmentMergeMode.Always;
            int priority = 0;
            string? onlyIf = null;
            string? onlyIfNot = null;
            List<ConditionAttribute> conditions = [];

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue; // structural: declares a condition namespace (e.g. xmlns:env)

                if (attribute.Name.NamespaceName is { Length: > 0 } ns)
                {
                    // a namespaced attribute is always a condition, resolved through the
                    // IEnvironmentConditionProvider registered for the namespace URI - even
                    // when its local name collides with a structural attribute (env:name)
                    conditions.Add(new ConditionAttribute(ns, attribute.Name.LocalName, attribute.Value,
                        DescribeAttribute(element, attribute)));
                }
                else if (attribute.Name.LocalName.Equals(NAME_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                    name = ParseName(attribute.Value);
                else if (attribute.Name.LocalName.Equals(ONLY_IF_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                    (mode, onlyIf) = ParseOnlyIf(attribute.Value, ENVIRONMENT_ELEMENT, allowElse: false);
                else if (attribute.Name.LocalName.Equals(ONLY_IF_NOT_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                    onlyIfNot = ParseOnlyIfNot(attribute.Value);
                else if (attribute.Name.LocalName.Equals(PRIORITY_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                    priority = ParsePriority(attribute.Value);
                else
                    conditions.Add(new ConditionAttribute(null, attribute.Name.LocalName, attribute.Value,
                        attribute.Name.LocalName));
            }

            var displayName = name ?? DescribeConditions(conditions) ?? $"anonymous #{++anonymous}";

            return new EnvironmentBlock
            {
                DisplayName = displayName,
                Name = name,
                MergeMode = mode,
                Priority = priority,
                OnlyIf = onlyIf,
                OnlyIfNot = onlyIfNot,
                ConditionAttributes = conditions,
                Content = NormalizeContent(element, displayName),
            };
        }

        /// <summary>
        /// Names an unnamed environment after its conditions, as they were written:
        /// <c>lid="closed" power="ac"</c>. Returns null when the block declares none,
        /// leaving it to the "anonymous #N" fallback.
        /// </summary>
        private static string? DescribeConditions(IReadOnlyList<ConditionAttribute> conditions)
            => conditions.Count > 0
                ? string.Join(' ', conditions.Select(condition => $"{condition.DisplayName}=\"{condition.Value}\""))
                : null;

        /// <summary>The attribute as written ("env:USER"), reconstructing the prefix from the
        /// namespace declaration in scope.</summary>
        private static string DescribeAttribute(XElement element, XAttribute attribute)
            => element.GetPrefixOfNamespace(attribute.Name.Namespace) is { Length: > 0 } prefix
                ? $"{prefix}:{attribute.Name.LocalName}"
                : attribute.Name.LocalName;

        /// <summary>
        /// Parses the onlyIf attribute. The keywords "always"/"never"/"else" select a
        /// <see cref="EnvironmentMergeMode"/>; any other value names another environment
        /// that must be applied for this block to apply (the positive counterpart of
        /// onlyIfNot). Environment names may therefore not be a keyword (see <see cref="ParseName"/>).
        /// </summary>
        private static (EnvironmentMergeMode Mode, string? Reference) ParseOnlyIf(string value, string element, bool allowElse)
        {
            switch (value.ToLowerInvariant())
            {
                case "always":
                    return (EnvironmentMergeMode.Always, null);

                case "never":
                    return (EnvironmentMergeMode.Never, null);

                case "else" when allowElse:
                    return (EnvironmentMergeMode.Else, null);

                case "else":
                    throw new ConfigurationValueException($"{ONLY_IF_ATTRIBUTE} = \"else\" is only " +
                        $"supported on <{DEFAULT_ENVIRONMENT_ELEMENT}>.");

                default:
                    if (string.IsNullOrWhiteSpace(value))
                        throw new ConfigurationValueException($"Invalid {ONLY_IF_ATTRIBUTE} on <{element}>; expected " +
                            $"\"always\", \"never\"{(allowElse ? ", \"else\"" : "")} or the name of another environment.");

                    // a non-keyword value is a positive reference; the block merges as usual while the target is applied
                    return (EnvironmentMergeMode.Always, value);
            }
        }

        private static string ParseName(string value)
        {
            if (RESERVED_NAMES.Contains(value, StringComparer.OrdinalIgnoreCase))
                throw new ConfigurationValueException($"'{value}' cannot be used as an environment name; " +
                    $"\"always\", \"never\" and \"else\" are reserved {ONLY_IF_ATTRIBUTE} keywords.");

            return value;
        }

        private static string ParseOnlyIfNot(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ConfigurationValueException($"{ONLY_IF_NOT_ATTRIBUTE} requires the name of another environment.");

            return value;
        }

        private static int ParsePriority(string value)
        {
            if (!int.TryParse(value, out int priority))
                throw new ConfigurationValueException($"Invalid {PRIORITY_ATTRIBUTE} = \"{value}\"; expected an integer.");

            return priority;
        }

        /// <summary>
        /// Validates all onlyIf/onlyIfNot references: the target name must exist, a block
        /// must not reference itself, and the combined reference graph must be acyclic
        /// (a cycle like A &#8596; B has no well-defined solution). Disabled (onlyIf="never")
        /// blocks behave like commented-out ones: their own references are not validated,
        /// and referencing them is allowed - such a target is simply never applied.
        /// </summary>
        private static void ValidateReferences(List<EnvironmentBlock> blocks)
        {
            static bool IsNamed(EnvironmentBlock block, string name)
                => block.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

            // a block's outgoing references (both kinds), each tagged with its attribute for error messages
            static IEnumerable<(string Attribute, string Target)> References(EnvironmentBlock block)
            {
                if (block.OnlyIf is string onlyIf)
                    yield return (ONLY_IF_ATTRIBUTE, onlyIf);

                if (block.OnlyIfNot is string onlyIfNot)
                    yield return (ONLY_IF_NOT_ATTRIBUTE, onlyIfNot);
            }

            var enabled = blocks.Where(block => block.MergeMode != EnvironmentMergeMode.Never).ToList();

            foreach (var block in enabled)
                foreach (var (attribute, target) in References(block))
                {
                    if (IsNamed(block, target))
                        throw new ConfigurationValueException($"Environment '{block.DisplayName}' must not " +
                            $"reference itself via {attribute}.");

                    if (!blocks.Any(other => IsNamed(other, target)))
                        throw new ConfigurationValueException($"Environment '{block.DisplayName}': " +
                            $"{attribute} references unknown environment '{target}'.");
                }

            // depth-first search over the combined reference graph; the path doubles as the "in progress" set
            HashSet<EnvironmentBlock> done = [];
            List<EnvironmentBlock> path = [];

            void Visit(EnvironmentBlock block)
            {
                if (done.Contains(block))
                    return;

                if (path.Contains(block))
                    throw new ConfigurationValueException($"Circular environment reference: " +
                        string.Join(" -> ", path.SkipWhile(b => b != block).Append(block).Select(b => $"'{b.DisplayName}'")) + ".");

                path.Add(block);

                foreach (var (_, target) in References(block))
                    foreach (var other in enabled.Where(other => IsNamed(other, target)))
                        Visit(other);

                path.RemoveAt(path.Count - 1);

                done.Add(block);
            }

            foreach (var block in enabled)
                Visit(block);
        }

        /// <summary>
        /// Returns the block's content as a detached, abstract &lt;SystemMonitor&gt; container
        /// (see <see cref="ConfigNode"/> — everything past the parser is independent of the
        /// XML representation). Since &lt;SystemMonitor&gt; is the formal configuration root,
        /// it may be omitted below the block and is then added transparently, without any
        /// attributes.
        /// </summary>
        private static ConfigNode NormalizeContent(XElement block, string displayName)
        {
            if (block.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
                throw new ConfigurationValueException($"Environment '{displayName}' must not contain text content.");

            var elements = block.Elements().ToList();

            ConfigNode content;

            if (elements.Any(element => Is(element, SYSTEM_MONITOR_ELEMENT)))
            {
                if (elements.Count != 1)
                    throw new ConfigurationValueException($"Environment '{displayName}': " +
                        $"when <{SYSTEM_MONITOR_ELEMENT}> is used, it must be the only child element.");

                content = XmlConfigurationReader.ToConfigNode(elements[0]);
            }
            else
            {
                content = new ConfigNode(SYSTEM_MONITOR_ELEMENT, ConfigNodeKind.Element);

                foreach (var element in elements)
                    content.Children.Add(XmlConfigurationReader.ToConfigNode(element));
            }

            return content;
        }

        private static bool Is(XElement element, string name)
            => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
