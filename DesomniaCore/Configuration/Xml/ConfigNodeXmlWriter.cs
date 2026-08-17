using MadWizard.Desomnia.Configuration.Model;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// Reconstructs the XML representation of a <see cref="ConfigNode"/> tree, using each
    /// node's <see cref="ConfigNode.Kind"/> to restore the written form (attribute vs.
    /// element). When blocks wrote the same value in different physical forms, the merged
    /// node keeps the form of the block that declared it first (the value itself follows
    /// the priority rules).
    /// </summary>
    internal static class ConfigNodeXmlWriter
    {
        public static XElement ToXElement(ConfigNode node)
        {
            var element = new XElement(node.Name);

            foreach (var child in node.Children)
            {
                if (child.Kind == ConfigNodeKind.Attribute)
                    element.Add(new XAttribute(child.Name, child.Value ?? string.Empty));
                else
                    element.Add(ToXElement(child));
            }

            // the empty string marks bare presence (<x/>) - real text content is non-empty
            if (node.Value is { Length: > 0 } text)
                element.Add(new XText(text));

            return element;
        }
    }
}
