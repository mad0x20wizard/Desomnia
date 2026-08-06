using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Environments;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class EnvironmentParserTests
    {
        private static EnvironmentParser.Result Parse(string xml)
            => EnvironmentParser.Parse(XDocument.Parse(xml));

        [Fact]
        public void ParsesVersionDebounceAndBlocks()
        {
            var result = Parse("""
                <EnvironmentMonitor version="1" debounce="10s">
                  <Environment name="home" network="10.0.0.0/8">
                    <NetworkMonitor interface="en0" />
                  </Environment>
                  <Environment power="ac">
                    <SystemMonitor onDemand="sleepless" />
                  </Environment>
                  <DefaultEnvironment>
                    <ProcessMonitor />
                  </DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            Assert.Equal("1", result.Version);
            Assert.Equal(TimeSpan.FromSeconds(10), result.Debounce);

            var blocks = result.Blocks;

            Assert.Equal(3, blocks.Count);

            Assert.Equal("home", blocks[0].DisplayName);
            Assert.Equal("home", blocks[0].Name);
            Assert.False(blocks[0].IsDefault);
            Assert.Equal([new ConditionAttribute(null, "network", "10.0.0.0/8", "network")], blocks[0].ConditionAttributes);

            Assert.Equal("power=\"ac\"", blocks[1].DisplayName);
            Assert.Null(blocks[1].Name);
            Assert.Equal([new ConditionAttribute(null, "power", "ac", "power")], blocks[1].ConditionAttributes);

            Assert.Equal("default", blocks[2].DisplayName);
            Assert.True(blocks[2].IsDefault);
            Assert.Empty(blocks[2].ConditionAttributes);
        }

        [Fact]
        public void OmittedSystemMonitor_IsWrappedTransparently()
        {
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment><NetworkMonitor name="WiFi" /></Environment>
                </EnvironmentMonitor>
                """).Blocks;

            var content = blocks[0].Content;

            Assert.Equal("SystemMonitor", content.Name);

            var child = Assert.Single(content.Children); // added transparently, without any attributes

            Assert.Equal(ConfigNodeKind.Element, child.Kind);
            Assert.Equal("NetworkMonitor", child.Name);
        }

        [Fact]
        public void ExplicitSystemMonitor_KeepsItsAttributes()
        {
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment><SystemMonitor onDemand="sleepless" /></Environment>
                </EnvironmentMonitor>
                """).Blocks;

            var attribute = Assert.Single(blocks[0].Content.Children);

            Assert.Equal(ConfigNodeKind.Attribute, attribute.Kind);
            Assert.True(attribute.HasName("onDemand"));
            Assert.Equal("sleepless", attribute.Value);
        }

        [Fact]
        public void InnerVersionAttribute_IsStripped()
        {
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment><SystemMonitor version="7" /></Environment>
                </EnvironmentMonitor>
                """).Blocks;

            Assert.DoesNotContain(blocks[0].Content.Children, child => child.HasName("version"));
        }

        [Fact]
        public void UnnamedEnvironment_IsNamedAfterItsConditions()
        {
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment onlyIf="docked" lid="closed" power="ac" priority="5" />
                  <Environment name="docked" />
                  <Environment onlyIfNot="docked" />
                </EnvironmentMonitor>
                """).Blocks;

            // meta attributes stay out of the name, conditions keep their document order
            Assert.Equal("lid=\"closed\" power=\"ac\"", blocks[0].DisplayName);

            // without any condition, the counter remains the fallback
            Assert.Equal("anonymous #1", blocks[2].DisplayName);
        }

        [Fact]
        public void DefaultDebounce_IsUsed()
        {
            var result = Parse("""
                <EnvironmentMonitor version="1"><DefaultEnvironment /></EnvironmentMonitor>
                """);

            Assert.Equal(EnvironmentParser.DEFAULT_DEBOUNCE, result.Debounce);
        }

        [Theory]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment /></EnvironmentMonitor>""", EnvironmentMergeMode.Always)]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment onlyIf="always" /></EnvironmentMonitor>""", EnvironmentMergeMode.Always)]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment onlyIf="else" /></EnvironmentMonitor>""", EnvironmentMergeMode.Else)]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment onlyIf="ELSE" /></EnvironmentMonitor>""", EnvironmentMergeMode.Else)]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment onlyIf="never" /></EnvironmentMonitor>""", EnvironmentMergeMode.Never)]
        [InlineData("""<EnvironmentMonitor version="1"><Environment onlyIf="never" /></EnvironmentMonitor>""", EnvironmentMergeMode.Never)]
        [InlineData("""<EnvironmentMonitor version="1"><Environment onlyIf="always" /></EnvironmentMonitor>""", EnvironmentMergeMode.Always)]
        internal void ParsesOnlyIf(string xml, EnvironmentMergeMode expected)
        {
            Assert.Equal(expected, Parse(xml).Blocks[0].MergeMode);
        }

        [Fact]
        public void ParsesOnlyIfNot()
        {
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment name="home" />
                  <Environment name="away" onlyIfNot="home" />
                  <DefaultEnvironment onlyIfNot="away" />
                </EnvironmentMonitor>
                """).Blocks;

            Assert.Null(blocks[0].OnlyIfNot);
            Assert.Equal("home", blocks[1].OnlyIfNot);
            Assert.Equal("away", blocks[2].OnlyIfNot);
        }

        [Fact]
        public void OnlyIfNot_MayReferenceADisabledEnvironment()
        {
            // a disabled target simply never applies; the reference stays valid
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment name="home" onlyIf="never" />
                  <Environment name="away" onlyIfNot="home" />
                </EnvironmentMonitor>
                """).Blocks;

            Assert.Equal("home", blocks[1].OnlyIfNot);
        }

        [Fact]
        public void OnlyIfNot_OnDisabledEnvironments_IsNotValidated()
        {
            // disabled blocks behave like commented-out ones - even a dangling reference is fine
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment name="home" onlyIf="never" onlyIfNot="nowhere" />
                  <DefaultEnvironment />
                </EnvironmentMonitor>
                """).Blocks;

            Assert.Equal("nowhere", blocks[0].OnlyIfNot);
        }

        [Fact]
        public void ParsesOnlyIf_AsEnvironmentReference()
        {
            // a non-keyword onlyIf value names another environment (a positive dependency);
            // the merge mode stays the default (Always)
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment name="vpn" />
                  <Environment name="work" onlyIf="vpn" />
                  <DefaultEnvironment onlyIf="work" />
                </EnvironmentMonitor>
                """).Blocks;

            Assert.Null(blocks[0].OnlyIf);

            Assert.Equal("vpn", blocks[1].OnlyIf);
            Assert.Equal(EnvironmentMergeMode.Always, blocks[1].MergeMode);

            Assert.Equal("work", blocks[2].OnlyIf);
        }

        [Fact]
        public void OnlyIf_MayReferenceADisabledEnvironment()
        {
            // a disabled target simply never applies; referencing it stays valid (this block then never applies either)
            var blocks = Parse("""
                <EnvironmentMonitor version="1">
                  <Environment name="home" onlyIf="never" />
                  <Environment name="guest" onlyIf="home" />
                </EnvironmentMonitor>
                """).Blocks;

            Assert.Equal("home", blocks[1].OnlyIf);
        }

        [Theory]
        [InlineData("""<EnvironmentMonitor version="1"><Environment /></EnvironmentMonitor>""", 0)]
        [InlineData("""<EnvironmentMonitor version="1"><Environment priority="7" /></EnvironmentMonitor>""", 7)]
        [InlineData("""<EnvironmentMonitor version="1"><Environment priority="-2" /></EnvironmentMonitor>""", -2)]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment priority="3" /></EnvironmentMonitor>""", 3)]
        public void ParsesPriority(string xml, int expected)
        {
            Assert.Equal(expected, Parse(xml).Blocks[0].Priority);
        }

        [Theory]
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment /></EnvironmentMonitor>""", ConflictResolution.Last)]
        [InlineData("""<EnvironmentMonitor version="1" onConflict="first"><DefaultEnvironment /></EnvironmentMonitor>""", ConflictResolution.First)]
        [InlineData("""<EnvironmentMonitor version="1" onConflict="Error"><DefaultEnvironment /></EnvironmentMonitor>""", ConflictResolution.Error)]
        internal void ParsesOnConflict(string xml, ConflictResolution expected)
        {
            Assert.Equal(expected, Parse(xml).OnConflict);
        }

        [Fact]
        public void ParsesOutputEffectiveXML()
        {
            var result = Parse("""
                <EnvironmentMonitor version="1" outputEffectiveXML="effective.xml"><DefaultEnvironment /></EnvironmentMonitor>
                """);

            Assert.Equal("effective.xml", result.OutputEffectiveXML);
        }

        [Fact]
        public void OmittedOutputEffectiveXML_IsNull()
        {
            var result = Parse("""
                <EnvironmentMonitor version="1"><DefaultEnvironment /></EnvironmentMonitor>
                """);

            Assert.Null(result.OutputEffectiveXML);
        }

        [Theory]
        [InlineData("""<EnvironmentMonitor><DefaultEnvironment /></EnvironmentMonitor>""")] // missing version
        [InlineData("""<EnvironmentMonitor version="1" debounce="soon"><DefaultEnvironment /></EnvironmentMonitor>""")] // bad debounce
        [InlineData("""<EnvironmentMonitor version="1" onConflict="ignore"><DefaultEnvironment /></EnvironmentMonitor>""")] // invalid onConflict
        [InlineData("""<EnvironmentMonitor version="1"><SystemMonitor /></EnvironmentMonitor>""")] // unknown child
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment /><DefaultEnvironment /></EnvironmentMonitor>""")] // two defaults
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment power="ac" /></EnvironmentMonitor>""")] // default with condition
        [InlineData("""<EnvironmentMonitor version="1"><Environment onlyIf="" /></EnvironmentMonitor>""")] // empty onlyIf
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment onlyIf="sometimes" /></EnvironmentMonitor>""")] // unknown onlyIf reference
        [InlineData("""<EnvironmentMonitor version="1"><Environment onlyIf="else" /></EnvironmentMonitor>""")] // "else" only on DefaultEnvironment
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="always" /></EnvironmentMonitor>""")] // reserved name
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="Never" /></EnvironmentMonitor>""")] // reserved name (case-insensitive)
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="else" /></EnvironmentMonitor>""")] // reserved name
        [InlineData("""<EnvironmentMonitor version="1"><Environment priority="high" /></EnvironmentMonitor>""")] // invalid priority
        [InlineData("""<EnvironmentMonitor version="1"><Environment onlyIfNot="" /></EnvironmentMonitor>""")] // empty onlyIfNot
        [InlineData("""<EnvironmentMonitor version="1"><Environment onlyIfNot="nowhere" /></EnvironmentMonitor>""")] // unknown onlyIfNot reference
        [InlineData("""<EnvironmentMonitor version="1"><DefaultEnvironment onlyIfNot="nowhere" /></EnvironmentMonitor>""")] // unknown reference on the default
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="home" onlyIfNot="home" /></EnvironmentMonitor>""")] // onlyIfNot self-reference
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="home" onlyIf="home" /></EnvironmentMonitor>""")] // onlyIf self-reference
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="a" onlyIfNot="b" /><Environment name="b" onlyIfNot="a" /></EnvironmentMonitor>""")] // onlyIfNot 2-cycle
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="a" onlyIfNot="b" /><Environment name="b" onlyIfNot="c" /><Environment name="c" onlyIfNot="a" /></EnvironmentMonitor>""")] // onlyIfNot 3-cycle
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="a" onlyIf="b" /><Environment name="b" onlyIf="a" /></EnvironmentMonitor>""")] // onlyIf 2-cycle
        [InlineData("""<EnvironmentMonitor version="1"><Environment name="a" onlyIf="b" /><Environment name="b" onlyIfNot="a" /></EnvironmentMonitor>""")] // mixed onlyIf/onlyIfNot cycle
        [InlineData("""<EnvironmentMonitor version="1"><Environment><SystemMonitor /><NetworkMonitor /></Environment></EnvironmentMonitor>""")] // SystemMonitor with siblings
        [InlineData("""<EnvironmentMonitor version="1"><Environment>text</Environment></EnvironmentMonitor>""")] // text content
        [InlineData("""<EnvironmentMonitor version="1" />""")] // no blocks
        [InlineData("""<EnvironmentMonitor version="1"><Environment><EnvironmentMonitor version="1" /></Environment></EnvironmentMonitor>""")] // nested
        public void InvalidStructure_Throws(string xml)
        {
            Assert.Throws<ConfigurationValueException>(() => Parse(xml));
        }
    }
}
