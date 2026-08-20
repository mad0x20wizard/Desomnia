using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// Queries over a configuration document the way the configuration vocabulary works: names
    /// are matched by their local part, case-insensitively, and configuration attributes carry
    /// no namespace (a namespaced attribute is a condition, never a setting).
    /// </summary>
    public static class XConfigurationExtensions
    {
        /// <summary>The element's attribute of that local name outside any namespace, or null.</summary>
        public static XAttribute? AttributeNamed(this XElement element, string localName)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentException.ThrowIfNullOrEmpty(localName);

            return element.Attributes().FirstOrDefault(attribute => attribute.Name.Namespace == XNamespace.None
                && attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The container's direct child elements of that local name, in document order.</summary>
        public static IEnumerable<XElement> ElementsNamed(this XContainer container, string localName)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentException.ThrowIfNullOrEmpty(localName);

            return container.Elements().Where(element => element.HasLocalName(localName));
        }

        /// <summary>The container's descendant elements (any depth) of that local name, in document
        /// order — in the environment layout a module's element appears once per environment block.</summary>
        public static IEnumerable<XElement> DescendantsNamed(this XContainer container, string localName)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentException.ThrowIfNullOrEmpty(localName);

            return container.Descendants().Where(element => element.HasLocalName(localName));
        }

        /// <summary>Whether the element's local name is <paramref name="localName"/> (case-insensitive).</summary>
        public static bool HasLocalName(this XElement element, string localName)
        {
            ArgumentNullException.ThrowIfNull(element);

            return element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
