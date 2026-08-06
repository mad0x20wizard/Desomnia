using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using System.Text;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The XML reader: the one place that understands the lexical form — global directives,
    /// namespace handling, and the conversion into the abstract <see cref="ConfigNode"/> tree.
    /// </summary>
    public class XmlConfigurationReaderTests
    {
        private static XmlConfigurationFile Read(string xml)
            => XmlConfigurationReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        [Fact]
        internal void Read_ExposesRootName_AndGlobalDirectives_InDocumentOrder()
        {
            var file = Read("""
                <?xml version="1.0" encoding="utf-8" ?>
                <?global useDBus="false" ?>
                <?global ProcessManager:pollInterval="2s" ?>
                <SystemMonitor version="1" />
                <?global PowerManager:watchOperation='sleep' ?>
                """);

            Assert.Equal("SystemMonitor", file.RootName);

            Assert.Collection(file.GlobalDirectives,
                directive => { Assert.Equal("useDBus", directive.Key); Assert.Equal("false", directive.Value); },
                directive => { Assert.Equal("ProcessManager:pollInterval", directive.Key); Assert.Equal("2s", directive.Value); },
                directive => { Assert.Equal("PowerManager:watchOperation", directive.Key); Assert.Equal("sleep", directive.Value); });
        }

        [Fact]
        internal void Read_WithoutDirectives_YieldsEmptyList()
        {
            Assert.Empty(Read("""<SystemMonitor version="1" />""").GlobalDirectives);
        }

        [Fact]
        internal void Read_GlobalDirectiveInsideTheRoot_Throws()
        {
            var ex = Assert.Throws<ConfigurationValueException>(() => Read("""
                <SystemMonitor version="1">
                  <NetworkMonitor>
                    <?global useDBus="false" ?>
                  </NetworkMonitor>
                </SystemMonitor>
                """));

            Assert.Contains("outside", ex.Message);
        }

        [Theory]
        [InlineData("useDBus")]                    // no value
        [InlineData("useDBus=false")]              // unquoted value
        [InlineData("""a="1" b="2" """)]           // two pairs
        [InlineData("""="value" """)]              // empty key
        [InlineData("")]                           // empty data
        internal void Read_InvalidGlobalDirective_Throws(string data)
        {
            Assert.Throws<ConfigurationValueException>(() => Read($"""
                <?global {data}?>
                <SystemMonitor version="1" />
                """));
        }

        [Fact]
        internal void Read_IgnoresForeignProcessingInstructions()
        {
            var file = Read("""
                <?something else="entirely" ?>
                <SystemMonitor version="1">
                  <?another one ?>
                </SystemMonitor>
                """);

            Assert.Empty(file.GlobalDirectives);
        }

        [Fact]
        internal void Read_MalformedXml_ThrowsConfigurationError()
        {
            Assert.Throws<ConfigurationValueException>(() => Read("<SystemMonitor><traffic must /></SystemMonitor"));
        }

        [Fact]
        internal void ToConfigNode_MapsAttributesAndElements_InDocumentOrder()
        {
            var node = Read("""
                <SystemMonitor version="1" timeout="5min">
                  <NetworkMonitor interface="en0" />
                </SystemMonitor>
                """).ToConfigNode();

            Assert.Equal("SystemMonitor", node.Name);
            Assert.Null(node.Value);

            Assert.Collection(node.Children,
                child => { Assert.Equal(ConfigNodeKind.Attribute, child.Kind); Assert.Equal("version", child.Name); Assert.Equal("1", child.Value); },
                child => { Assert.Equal(ConfigNodeKind.Attribute, child.Kind); Assert.Equal("timeout", child.Name); Assert.Equal("5min", child.Value); },
                child =>
                {
                    Assert.Equal(ConfigNodeKind.Element, child.Kind);
                    Assert.Equal("NetworkMonitor", child.Name);

                    var attribute = Assert.Single(child.Children);
                    Assert.Equal("interface", attribute.Name);
                    Assert.Equal("en0", attribute.Value);
                });
        }

        [Fact]
        internal void ToConfigNode_SkipsNamespaceDeclarations_AndUsesLocalNames()
        {
            var node = Read("""
                <SystemMonitor version="1" xmlns:env="environment:process">
                  <env:Custom env:flag="on" />
                </SystemMonitor>
                """).ToConfigNode();

            Assert.Collection(node.Children,
                child => Assert.Equal("version", child.Name), // no xmlns:env node
                child =>
                {
                    Assert.Equal("Custom", child.Name); // local name

                    var attribute = Assert.Single(child.Children);
                    Assert.Equal("flag", attribute.Name);
                });
        }

        [Fact]
        internal void ToConfigNode_BareElement_HasEmptyValue_ForPresenceBinding()
        {
            var node = Read("""
                <SystemMonitor>
                  <Ethernet />
                  <WiFi ssid="lounge" />
                </SystemMonitor>
                """).ToConfigNode();

            Assert.Equal(string.Empty, node.Children[0].Value); // bare: presence must bind
            Assert.Null(node.Children[1].Value);                // has attributes: no own value
        }

        [Fact]
        internal void ToConfigNode_TextContent_CoexistsWithChildren()
        {
            var node = Read("""
                <SystemMonitor>
                  <Process name="Moonlight" onStart="wake://pc">/Applications/Moonlight.app</Process>
                </SystemMonitor>
                """).ToConfigNode();

            var process = Assert.Single(node.Children);

            Assert.Equal("/Applications/Moonlight.app", process.Value);
            Assert.Equal(2, process.Children.Count);
            Assert.Equal("Moonlight", process.ItemName);
        }

        [Fact]
        internal void ToConfigNode_WhitespaceOnlyText_DoesNotCount()
        {
            var node = Read("""
                <SystemMonitor>
                  <NetworkMonitor>
                    <Host name="a" />
                  </NetworkMonitor>
                </SystemMonitor>
                """).ToConfigNode();

            Assert.Null(Assert.Single(node.Children).Value);
        }

        [Fact]
        internal void ConfigNode_RoundTripsThroughTheXmlWriter()
        {
            var node = Read("""
                <SystemMonitor version="1">
                  <Process name="Moonlight">/Applications/Moonlight.app</Process>
                  <Ethernet />
                </SystemMonitor>
                """).ToConfigNode();

            var xml = ConfigNodeXmlWriter.ToXElement(node);

            Assert.Equal("1", xml.Attribute("version")!.Value);
            Assert.Equal("/Applications/Moonlight.app", xml.Element("Process")!.Value);
            Assert.Equal("Moonlight", xml.Element("Process")!.Attribute("name")!.Value);
            Assert.True(xml.Element("Ethernet")!.IsEmpty);
        }
    }
}
