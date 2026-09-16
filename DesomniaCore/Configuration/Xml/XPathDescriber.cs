using MadWizard.Desomnia.Configuration.Model;
using System.Text;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// Renders the position of a node as an XPath-like expression the user can match against the
    /// file: <c>/EnvironmentMonitor/DefaultEnvironment/NetworkMonitor[@name='Ethernet']/@watchPort</c>.
    /// Elements are keyed by their <c>name</c> attribute when they have one (that is how the
    /// configuration identifies collection items), otherwise by position among same-name
    /// siblings when there is more than one.
    /// </summary>
    internal static class XPathDescriber
    {
        public static string Describe(XObject node)
        {
            ArgumentNullException.ThrowIfNull(node);

            return node switch
            {
                XDocument => "/",
                XElement element => DescribeElement(element),
                XAttribute attribute => $"{DescribeParent(attribute.Parent)}/@{QualifiedName(attribute)}",
                XText text => $"{DescribeParent(text.Parent)}/text()",
                XComment comment => $"{DescribeParent(comment.Parent)}/comment(){Position(comment)}",
                XProcessingInstruction pi => $"{DescribeParent(pi.Parent)}/processing-instruction('{pi.Target}'){Position(pi)}",
                XDocumentType => "/",

                _ => throw new NotSupportedException($"Cannot describe a node of type {node.GetType().Name}."),
            };
        }

        // the ancestor chain rendered root-first; a detached subtree simply starts at its top
        private static string DescribeElement(XElement element)
        {
            StringBuilder path = new();

            foreach (var step in element.AncestorsAndSelf().Reverse())
            {
                path.Append('/').Append(QualifiedName(step));

                if (ItemName(step) is XAttribute name)
                {
                    path.Append("[@").Append(name.Name.LocalName).Append('=').Append(Quote(name.Value)).Append(']');
                }
                else if (step.Parent is XElement parent && parent.Elements(step.Name).Skip(1).Any())
                {
                    int position = parent.Elements(step.Name).TakeWhile(sibling => sibling != step).Count() + 1;

                    path.Append('[').Append(position).Append(']');
                }
            }

            return path.ToString();
        }

        // the item name attribute, matched case-insensitively like every attribute name in
        // this codebase (the binder treats <X Name="..."> as a named collection item, too)
        private static XAttribute? ItemName(XElement element)
            => element.Attributes().FirstOrDefault(attribute => attribute.Name.Namespace == XNamespace.None
                && attribute.Name.LocalName.Equals(ConfigNode.ItemNameAttribute, StringComparison.OrdinalIgnoreCase));

        // an element parent renders as its own path; the document itself renders as nothing,
        // so top-level nodes come out as "/comment()" and the like
        private static string DescribeParent(XElement? parent)
            => parent is not null ? DescribeElement(parent) : string.Empty;

        // [n] among the same-kind siblings under the same container (element or document),
        // only when there is more than one of them - for processing instructions "same kind"
        // means the same target, as processing-instruction('system')[2] reads in XPath
        private static string Position(XNode node)
        {
            XContainer? container = node.Parent ?? (XContainer?)node.Document;

            var siblings = container?.Nodes().Where(sibling => sibling.NodeType == node.NodeType
                && (node is not XProcessingInstruction pi || sibling is XProcessingInstruction other && other.Target == pi.Target)).ToList();

            if (siblings is null || siblings.Count < 2)
                return string.Empty;

            return $"[{siblings.IndexOf(node) + 1}]";
        }

        private static string QualifiedName(XElement element)
            => element.Name.Namespace != XNamespace.None
                && element.GetPrefixOfNamespace(element.Name.Namespace) is { Length: > 0 } prefix
                ? $"{prefix}:{element.Name.LocalName}"
                : element.Name.LocalName;

        private static string QualifiedName(XAttribute attribute)
        {
            if (attribute.IsNamespaceDeclaration)
            {
                // xmlns="..." is the default-namespace declaration; xmlns:p="..." declares a prefix
                return attribute.Name.Namespace == XNamespace.None ? "xmlns" : $"xmlns:{attribute.Name.LocalName}";
            }

            if (attribute.Name.Namespace != XNamespace.None
                && attribute.Parent?.GetPrefixOfNamespace(attribute.Name.Namespace) is { Length: > 0 } prefix)
                return $"{prefix}:{attribute.Name.LocalName}";

            return attribute.Name.LocalName;
        }

        // XPath has no escaping inside string literals: pick the quote the value does not contain
        private static string Quote(string value)
            => value.Contains('\'') ? $"\"{value}\"" : $"'{value}'";
    }
}
