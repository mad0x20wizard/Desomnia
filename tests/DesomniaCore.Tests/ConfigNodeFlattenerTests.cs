using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using System.Text;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The flattener produces the data shape of the stock JSON/YAML providers — collection
    /// items keyed by index (or by name, for dictionary targets), the name attribute pure
    /// item data — plus presence values for bare elements and document order all the way
    /// into <c>IConfiguration.GetChildren()</c>. Where no name attribute is involved the
    /// shape coincides with the stock XmlConfigurationProvider, which some tests still
    /// assert against; the name handling deliberately diverges from it.
    /// </summary>
    public class ConfigNodeFlattenerTests
    {
        #region Harness

        /// <summary>A minimal provider over the flattened pairs — the same shape the real
        /// providers use (ordered data + document-order child keys).</summary>
        private sealed class FlattenedSource(OrderedConfigurationData data) : IConfigurationSource
        {
            public IConfigurationProvider Build(IConfigurationBuilder builder) => new FlattenedProvider(data);
        }

        private sealed class FlattenedProvider(OrderedConfigurationData data) : ConfigurationProvider
        {
            public override void Load() => Data = data.Data;

            public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
                => data.GetChildKeys(earlierKeys, parentPath);
        }

        private static IConfigurationRoot Flatten(string xml, CollectionElements? collections = null)
        {
            var file = XmlConfigurationReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

            var pairs = ConfigNodeFlattener.Flatten(file.ToConfigNode(), collections ?? CollectionElements.Empty);

            return new ConfigurationBuilder().Add(new FlattenedSource(new OrderedConfigurationData(pairs))).Build();
        }

        private static IConfigurationRoot Stock(string xml)
            => new ConfigurationBuilder().Add(new XmlStreamConfigurationSource
            {
                Stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)),
            }).Build();

        /// <summary>Same keys, same values — order-insensitive (the stock provider sorts,
        /// ours keeps document order; equality of the sets is what binding compatibility needs).</summary>
        private static void AssertSameConfiguration(IConfigurationRoot expected, IConfigurationRoot actual)
        {
            static List<KeyValuePair<string, string?>> Sorted(IConfigurationRoot root)
                => [.. root.AsEnumerable().OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)];

            Assert.Equal(Sorted(expected), Sorted(actual));
        }

        #endregion

        #region Layout: JSON-parity data shape

        // The layout parallels the stock JSON/YAML providers, NOT the stock XML provider:
        // collection items are keyed by index (or, for dictionary targets, by name), and a
        // name attribute is ordinary item data - never a key segment. Where no name attribute
        // is involved, the shape coincides with the stock XML provider (asserted against it).

        [Fact]
        public void AttributesAndNestedElements_MatchTheStockLayout()
        {
            const string xml = """
                <SystemMonitor timeout="00:05:00">
                  <NetworkMonitor interface="en0">
                    <Discovery scan="true" />
                  </NetworkMonitor>
                </SystemMonitor>
                """;

            AssertSameConfiguration(Stock(xml), Flatten(xml));
        }

        [Fact]
        public void TextContent_WithAttributesAndChildren_FlattensAsASingularObject()
        {
            var ours = Flatten("""
                <SystemMonitor>
                  <Process name="Moonlight" onStart="wake://pc">/Applications/Moonlight.app<Filter type="user" /></Process>
                </SystemMonitor>
                """);

            // the name attribute is a member of the object - the stock XML provider would
            // splice it into the path ("Process:Moonlight:...")
            Assert.Equal("/Applications/Moonlight.app", ours["Process"]);
            Assert.Equal("Moonlight", ours["Process:name"]);
            Assert.Equal("wake://pc", ours["Process:onStart"]);
            Assert.Equal("user", ours["Process:Filter:type"]);
        }

        [Fact]
        public void NameAttributes_StayItemData_NeverKeySegments()
        {
            var ours = Flatten("""
                <SystemMonitor>
                  <RemoteHost name="alpha" port="1" />
                  <RemoteHost name="beta" port="2" />
                </SystemMonitor>
                """);

            Assert.Equal("1", ours["RemoteHost:0:port"]);
            Assert.Equal("alpha", ours["RemoteHost:0:name"]);
            Assert.Equal("beta", ours["RemoteHost:1:name"]);

            Assert.Null(ours["RemoteHost:alpha:port"]); // no name-keyed layout
        }

        [Fact]
        public void RepeatedNamelessSiblings_GetIndexSegments_LikeTheStockProvider()
        {
            const string xml = """
                <SystemMonitor>
                  <Service port="1" />
                  <Service port="2" />
                </SystemMonitor>
                """;

            var ours = Flatten(xml);

            AssertSameConfiguration(Stock(xml), ours);

            Assert.Equal("1", ours["Service:0:port"]);
            Assert.Equal("2", ours["Service:1:port"]);
        }

        [Fact]
        public void SingleNamelessSibling_GetsNoIndex_LikeTheStockProvider()
        {
            const string xml = """
                <SystemMonitor>
                  <NetworkMonitor interface="en0" />
                </SystemMonitor>
                """;

            var ours = Flatten(xml);

            AssertSameConfiguration(Stock(xml), ours);

            Assert.Equal("en0", ours["NetworkMonitor:interface"]);
        }

        #endregion

        #region Layout: dictionary collections

        [Fact]
        public void DictionaryItems_KeyByTheirName()
        {
            var collections = CollectionElements.Of().WithDictionaries("RemoteHost");

            var ours = Flatten("""
                <SystemMonitor>
                  <RemoteHost name="beta" port="2" />
                  <RemoteHost Name="alpha" port="1" />
                </SystemMonitor>
                """, collections);

            Assert.Equal("2", ours["RemoteHost:beta:port"]);
            Assert.Equal("1", ours["RemoteHost:alpha:port"]); // the name attribute matches case-insensitively

            // written order survives into GetChildren (the stock providers would sort)
            Assert.Equal(["beta", "alpha"], ours.GetSection("RemoteHost").GetChildren().Select(section => section.Key));
        }

        [Fact]
        public void DictionaryItems_RequireANameAttribute()
        {
            var collections = CollectionElements.Of().WithDictionaries("RemoteHost");

            Assert.Throws<ConfigurationValueException>(() => Flatten("""
                <SystemMonitor>
                  <RemoteHost port="1" />
                </SystemMonitor>
                """, collections));
        }

        #endregion

        #region The old pipeline's XML fixups, now flattener behavior

        [Fact]
        public void BareElement_EmitsAnEmptyValue_LikeTheOldEmptyNodeFixup()
        {
            // the old pipeline rewrote <Ethernet/> to <Ethernet></Ethernet> before the stock
            // parse; the flattener emits the presence value directly
            var ours = Flatten("""
                <SystemMonitor>
                  <Ethernet />
                </SystemMonitor>
                """);

            var stock = Stock("""
                <SystemMonitor>
                  <Ethernet></Ethernet>
                </SystemMonitor>
                """);

            AssertSameConfiguration(stock, ours);

            Assert.Equal(string.Empty, ours["Ethernet"]);
        }

        [Fact]
        public void ArrayCollectionItems_KeyByIndex_NamedOrNot()
        {
            var collections = CollectionElements.Of("Process");

            var ours = Flatten("""
                <ProcessMonitor>
                  <Process pattern="a" />
                  <Process pattern="b" />
                  <Process name="explicit" pattern="c" />
                </ProcessMonitor>
                """, collections);

            // like a JSON array: the index is the key, the name (where written) is item data
            Assert.Equal("a", ours["Process:0:pattern"]);
            Assert.Equal("b", ours["Process:1:pattern"]);
            Assert.Equal("c", ours["Process:2:pattern"]);

            Assert.Null(ours["Process:0:name"]); // nothing is synthesized - Name binds null
            Assert.Equal("explicit", ours["Process:2:name"]);
        }

        [Fact]
        public void SingleCollectionItem_StillKeysByIndex()
        {
            // without the index, a single item would flatten its attributes directly into
            // the collection section, indistinguishable from attributes of the collection
            var collections = CollectionElements.Derive([typeof(FakeMonitorConfig)]);

            var ours = Flatten("""
                <SystemMonitor>
                  <Watch pattern="only" />
                </SystemMonitor>
                """, collections);

            Assert.Equal("only", ours["Watch:0:pattern"]);
        }

        [Fact]
        public void Derive_ClassifiesDictionaries_AndArrays()
        {
            var collections = CollectionElements.Derive([typeof(FakeMonitorConfig)]);

            Assert.Equal(CollectionKind.Array, collections.KindOf("Watch"));
            Assert.Equal(CollectionKind.Dictionary, collections.KindOf("NamedWatch"));
            Assert.Null(collections.KindOf("Unrelated"));
        }

        private sealed class FakeMonitorConfig
        {
            public List<FakeWatchInfo> Watch { get; } = [];

            public Dictionary<string, FakeWatchInfo> NamedWatch { get; } = [];
        }

        private sealed class FakeWatchInfo
        {
            public string? Pattern { get; set; }
        }

        #endregion

        #region Document order (GetChildren)

        [Fact]
        public void GetChildren_PreservesDocumentOrder()
        {
            var ours = Flatten("""
                <SystemMonitor>
                  <Zulu />
                  <Alpha />
                  <Mike />
                </SystemMonitor>
                """);

            Assert.Equal(["Zulu", "Alpha", "Mike"], ours.GetChildren().Select(section => section.Key));
        }

        [Fact]
        public void GetChildren_KeepsCollectionItems_InWrittenOrder()
        {
            var ours = Flatten("""
                <SystemMonitor>
                  <Host name="zulu" />
                  <Host name="alpha" />
                </SystemMonitor>
                """, CollectionElements.Of("Host"));

            Assert.Equal(["0", "1"], ours.GetSection("Host").GetChildren().Select(section => section.Key));

            // written order backs the index order - "zulu" stays the first item
            Assert.Equal("zulu", ours["Host:0:name"]);
        }

        #endregion

        [Fact]
        public void ValueAndChildren_Coexist_OnTheSameSection()
        {
            var ours = Flatten("""
                <SystemMonitor>
                  <Process onStart="x">/bin/tool</Process>
                </SystemMonitor>
                """);

            var section = ours.GetSection("Process");

            Assert.Equal("/bin/tool", section.Value);
            Assert.Equal("x", section["onStart"]);
        }

        [Fact]
        public void DuplicateKeys_AreAConfigurationError()
        {
            Assert.Throws<ConfigurationValueException>(() => Flatten("""
                <SystemMonitor>
                  <Watch pattern="attr"><pattern>element</pattern></Watch>
                </SystemMonitor>
                """));
        }
    }
}
