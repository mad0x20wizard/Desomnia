using MadWizard.Desomnia.Configuration;
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
    /// The flattener must reproduce the key layout the stock XmlConfigurationProvider
    /// produces (every module's configuration type binds against that layout), while adding
    /// what the old pipeline achieved through XML rewriting: presence values for bare
    /// elements and synthesized names for nameless collection items — and, on top, document
    /// order all the way into <c>IConfiguration.GetChildren()</c>.
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

        private static IConfigurationRoot Flatten(string xml, CollectionElementRegistry? collections = null)
        {
            var file = XmlConfigurationReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

            var pairs = ConfigNodeFlattener.Flatten(file.ToConfigNode(), collections ?? new CollectionElementRegistry());

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

        #region Stock layout compatibility

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
        public void TextContent_WithAttributesAndChildren_MatchesTheStockLayout()
        {
            const string xml = """
                <SystemMonitor>
                  <Process name="Moonlight" onStart="wake://pc">/Applications/Moonlight.app<Filter type="user" /></Process>
                </SystemMonitor>
                """;

            AssertSameConfiguration(Stock(xml), Flatten(xml));
        }

        [Fact]
        public void NameAttributes_FoldIntoThePath_LikeTheStockProvider()
        {
            const string xml = """
                <SystemMonitor>
                  <RemoteHost name="alpha" port="1" />
                  <RemoteHost name="beta" port="2" />
                </SystemMonitor>
                """;

            var ours = Flatten(xml);

            AssertSameConfiguration(Stock(xml), ours);

            Assert.Equal("1", ours["RemoteHost:alpha:port"]);
            Assert.Equal("beta", ours["RemoteHost:beta:name"]); // the name attribute stays a child key
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

        [Fact]
        public void CaseInsensitiveNameAttribute_FoldsLikeTheStockProvider()
        {
            const string xml = """
                <SystemMonitor>
                  <RemoteHost Name="alpha" port="1" />
                </SystemMonitor>
                """;

            AssertSameConfiguration(Stock(xml), Flatten(xml));
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
        public void NamelessCollectionItems_GetSynthesizedNames_LikeTheOldNameFixup()
        {
            var collections = new CollectionElementRegistry();
            collections.AddCollectionNameBuilder("Process", (element, nr) => $"{element}#{nr}");

            var ours = Flatten("""
                <ProcessMonitor>
                  <Process pattern="a" />
                  <Process pattern="b" />
                  <Process name="explicit" pattern="c" />
                </ProcessMonitor>
                """, collections);

            // the old pipeline added name="Process#N" attributes before the stock parse
            var stock = Stock("""
                <ProcessMonitor>
                  <Process pattern="a" name="Process#1" />
                  <Process pattern="b" name="Process#2" />
                  <Process name="explicit" pattern="c" />
                </ProcessMonitor>
                """);

            AssertSameConfiguration(stock, ours);

            Assert.Equal("a", ours["Process:Process#1:pattern"]);
            Assert.Equal("Process#1", ours["Process:Process#1:name"]); // synthesized name binds like a written one
            Assert.Equal("c", ours["Process:explicit:pattern"]);
        }

        [Fact]
        public void SingleNamelessCollectionItem_StillGetsAName()
        {
            // without the synthesis, a single nameless item would flatten its attributes
            // directly into the collection section, indistinguishable from attributes
            var collections = new CollectionElementRegistry();
            collections.AddCollectionElementsOf(typeof(FakeMonitorConfig));

            var ours = Flatten("""
                <SystemMonitor>
                  <Watch pattern="only" />
                </SystemMonitor>
                """, collections);

            Assert.Equal("only", ours["Watch:Watch#1:pattern"]);
        }

        private sealed class FakeMonitorConfig
        {
            public List<FakeWatchInfo> Watch { get; } = [];
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
                  <Host name="item#10" />
                  <Host name="item#2" />
                </SystemMonitor>
                """);

            // the stock ConfigurationKeyComparer would sort "item#10" before "item#2"
            Assert.Equal(["item#10", "item#2"], ours.GetSection("Host").GetChildren().Select(section => section.Key));
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
