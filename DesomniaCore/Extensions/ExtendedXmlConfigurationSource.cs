using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.Extensions.Configuration.Xml
{
    public class ExtendedXmlConfigurationSource : XmlConfigurationSource
    {
        internal readonly List<string> EnumAttributes = [];
        internal readonly Dictionary<string, AttributeMapping> BooleanAttributes = [];
        internal readonly List<string> NamelessCollectionElements = [];

        public ExtendedXmlConfigurationSource(string path, bool optional = false, bool reloadOnChange = false)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException($"path = {path}");

            Path = path;
            Optional = optional;
            ReloadOnChange = reloadOnChange;

            ResolveFileProvider();
        }

        public ExtendedXmlConfigurationSource AddEnumAttribute(string name)
        {
            EnumAttributes.Add(name);
            return this;
        }

        public ExtendedXmlConfigurationSource AddBooleanAttribute(string name, AttributeMapping mapping)
        {
            BooleanAttributes.Add(name, mapping);
            return this;
        }

        public ExtendedXmlConfigurationSource AddNamelessCollectionElement(string name)
        {
            NamelessCollectionElements.Add(name);
            return this;
        }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);

            return new CustomXmlConfigurationProvider(this);
        }

        public class AttributeMapping : IIEnumerable<KeyValuePair<string, string>>
        {
            readonly Dictionary<string, string> _mappings = [];

            public string this[string key] { get => _mappings[key]; set { _mappings[key] = value; } }

            IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
            {
                return _mappings.GetEnumerator();
            }
        }
    }

    partial class CustomXmlConfigurationProvider(ExtendedXmlConfigurationSource source) : XmlConfigurationProvider(source)
    {
        internal const string EMPTY_ATTRIBUTE_NAME = "__empty";
        internal const string TEXT_ATTRIBUTE_NAME = "text";
        internal const string NAME_ATTRIBUTE_NAME = "name";

        internal static Regex TimeSpanRegex = TimeSpanPattern();

        public override void Load(Stream stream)
        {
            if (source.BooleanAttributes.Count > 0)
                stream = ReplaceBooleanAttributes(stream);

            using MemoryStream memory = new();

            XDocument xml = XDocument.Load(stream);
            TraverseNodes(xml.Root!);
            xml.Save(memory);

            memory.Position = 0;

            base.Load(memory);

            stream.Dispose();
        }

        private Stream ReplaceBooleanAttributes(Stream input)
        {
            // 1. Read Stream into string
            string content;
            using (var reader = new StreamReader(input, Encoding.UTF8, true, 1024, leaveOpen: true))
            {
                input.Position = 0; // Ensure we're at the start
                content = reader.ReadToEnd();
            }

            // 2. Perform replacements
            foreach (var replacement in source.BooleanAttributes)
            {
                var key = " " + replacement.Key;
                var value = " " + string.Join(' ', replacement.Value.Select(attribute => $"{attribute.Key}=\"{attribute.Value}\""));

                // IMPROVE this is a simple string replacement, which may not be safe for all XML content

                content = content.Replace(key, value, StringComparison.InvariantCultureIgnoreCase);
            }

            // 3. Convert string back to Stream
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        private void TraverseNodes(XElement element)
        {
            SupportNamelessCollectionElements(element);

            SupportTextNode(element);
            SupportEmptyNode(element);

            SupportTimeSpanAttribute(element);
            SupportEnumAttributes(element);

            foreach (XElement childElement in element.Elements())
                TraverseNodes(childElement);
        }

        private void SupportEnumAttributes(XElement element)
        {
            foreach (var attribute in element.Attributes())
            {
                if (source.EnumAttributes.Contains(attribute.Name.LocalName))
                {
                    attribute.Value = attribute.Value.Replace("|", ",").Trim();
                }
            }
        }

        private void SupportNamelessCollectionElements(XElement element)
        {
            Dictionary<string, uint>? counters = null;

            foreach (var child in element.Elements())
            {
                var elementName = child.Name.LocalName;

                if (source.NamelessCollectionElements.Contains(elementName))
                {
                    if (child.Attribute(NAME_ATTRIBUTE_NAME) is null)
                    {
                        if (!(counters ??= []).ContainsKey(elementName))
                            counters[elementName] = 0;

                        counters[elementName]++;

                        child.Add(new XAttribute(NAME_ATTRIBUTE_NAME, $"{elementName}#{counters[elementName]}"));
                    }
                }
            }
        }

        private static void SupportTextNode(XElement element)
        {
            var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));

            if (!string.IsNullOrWhiteSpace(text))
            {
                element.Add(new XAttribute(TEXT_ATTRIBUTE_NAME, text)); // make text content accessible
            }
        }

        private static void SupportEmptyNode(XElement element)
        {
            if (!(element.HasAttributes || element.HasElements))
            {
                element.Add(new XAttribute(EMPTY_ATTRIBUTE_NAME, "true")); // allow empty nodes
            }
        }

        private static void SupportTimeSpanAttribute(XElement element)
        {
            foreach (var attribute in element.Attributes())
            {
                if (ISO8601TimeSpanPattern().Match(attribute.Value).Success)
                {
                    TimeSpan time = XmlConvert.ToTimeSpan(attribute.Value);

                    attribute.Value = time.ToString();
                }

                else if (TimeSpanPattern().Match(attribute.Value) is Match match && match.Success)
                {
                    TimeSpan time = TimeSpan.Zero;
                    if (match.Groups.TryGetValue("hours", out var hours) && hours.Success)
                        time += TimeSpan.FromHours(int.Parse(hours.Value));
                    if (match.Groups.TryGetValue("minutes", out var minutes) && minutes.Success)
                        time += TimeSpan.FromMinutes(int.Parse(minutes.Value));
                    if (match.Groups.TryGetValue("seconds", out var seconds) && seconds.Success)
                        time += TimeSpan.FromSeconds(int.Parse(seconds.Value));
                    if (match.Groups.TryGetValue("milliseconds", out var milliseconds) && milliseconds.Success)
                        time += TimeSpan.FromMilliseconds(int.Parse(milliseconds.Value));

                    attribute.Value = time.ToString();
                }
            }
        }

        internal static string NormalizeTimeSpanVariations(string value)
        {
            if (ISO8601TimeSpanPattern().Match(value).Success)
            {
                TimeSpan time = XmlConvert.ToTimeSpan(value);

                return time.ToString();
            }

            else if (TimeSpanPattern().Match(value) is Match match && match.Success)
            {
                TimeSpan time = TimeSpan.Zero;
                if (match.Groups.TryGetValue("hours", out var hours) && hours.Success)
                    time += TimeSpan.FromHours(int.Parse(hours.Value));
                if (match.Groups.TryGetValue("minutes", out var minutes) && minutes.Success)
                    time += TimeSpan.FromMinutes(int.Parse(minutes.Value));
                if (match.Groups.TryGetValue("seconds", out var seconds) && seconds.Success)
                    time += TimeSpan.FromSeconds(int.Parse(seconds.Value));
                if (match.Groups.TryGetValue("milliseconds", out var milliseconds) && milliseconds.Success)
                    time += TimeSpan.FromMilliseconds(int.Parse(milliseconds.Value));

                return time.ToString();
            }

            return value;
        }

        [GeneratedRegex(@"^P(?=\d|T\d)(\d+Y)?(\d+M)?(\d+D)?(T(\d+H)?(\d+M)?(\d+S)?)?$")]
        private static partial Regex ISO8601TimeSpanPattern();


        [GeneratedRegex(@"^(?=.*\d+(?:h|min|s|ms))(?:(?<hours>\d+)h)?(?:(?<minutes>\d+)min)?(?:(?<seconds>\d+)s)?(?:(?<milliseconds>\d+)ms)?$")]
        private static partial Regex TimeSpanPattern();
    }
}