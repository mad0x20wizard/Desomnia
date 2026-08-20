using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Environments;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class ConfigMergerTests
    {
        private static readonly CollectionElementRegistry Collections = CreateCollections();

        private static CollectionElementRegistry CreateCollections()
        {
            var collections = new CollectionElementRegistry();

            foreach (var element in new[] { "NetworkMonitor", "RemoteHost", "Process" })
                collections.AddCollectionNameBuilder(element, (name, nr) => $"{name}#{nr}");

            return collections;
        }

        private static EnvironmentBlock Block(string name, string xml, int priority = 0) => new()
        {
            DisplayName = name,
            Priority = priority,
            ConditionAttributes = [],
            Content = XmlConfigurationReader.ToConfigNode(XElement.Parse(xml)),
        };

        private static ConfigNode Merge(params EnvironmentBlock[] blocks)
            => ConfigMerger.Merge(blocks, Collections, ConflictResolution.Last);

        private static ConfigNode Merge(ConflictResolution onConflict, params EnvironmentBlock[] blocks)
            => ConfigMerger.Merge(blocks, Collections, onConflict);

        private static string? Attribute(ConfigNode node, string name)
            => node.Children.FirstOrDefault(child => child.Kind == ConfigNodeKind.Attribute && child.HasName(name))?.Value;

        private static List<ConfigNode> Elements(ConfigNode node, string name)
            => node.Children.Where(child => child.Kind == ConfigNodeKind.Element && child.HasName(name)).ToList();

        [Fact]
        public void LaterBlockWins_OnConflictingAttributes()
        {
            var result = Merge(
                Block("a", """<SystemMonitor onDemand="sleepless" timeout="2min" />"""),
                Block("b", """<SystemMonitor onDemand="wake" />"""));

            Assert.Equal("wake", Attribute(result, "onDemand"));
            Assert.Equal("2min", Attribute(result, "timeout")); // untouched
        }

        [Fact]
        public void NamedCollectionItems_AreMergedByName()
        {
            var result = Merge(
                Block("a", """<SystemMonitor><NetworkMonitor name="WiFi" interface="en0" /></SystemMonitor>"""),
                Block("b", """<SystemMonitor><NetworkMonitor name="WiFi" network="10.0.0.0/24" /><NetworkMonitor name="Ethernet" /></SystemMonitor>"""));

            var monitors = Elements(result, "NetworkMonitor");

            Assert.Equal(2, monitors.Count);
            Assert.Equal("en0", Attribute(monitors[0], "interface"));
            Assert.Equal("10.0.0.0/24", Attribute(monitors[0], "network"));
            Assert.Equal("Ethernet", monitors[1].ItemName);
        }

        [Fact]
        public void ValueFilledIntoAValuelessNode_CarriesTheFillersPriority()
        {
            // A@5 creates <Sleep> without a value; B@1 contributes the value - later
            // conflicts must resolve against B@1 (the value's contributor), not against
            // A@5 (the node's creator), so C@3 wins here
            var result = Merge(
                Block("a", """<SystemMonitor><Sleep grace="1s" /></SystemMonitor>""", priority: 5),
                Block("b", """<SystemMonitor><Sleep>10min</Sleep></SystemMonitor>""", priority: 1),
                Block("c", """<SystemMonitor><Sleep>30min</Sleep></SystemMonitor>""", priority: 3));

            Assert.Equal("30min", Assert.Single(Elements(result, "Sleep")).Value);
        }

        [Fact]
        public void PresenceOnlyFill_DoesNotClaimTheValueProvenance()
        {
            // a bare <Sleep/> asserts presence, not content - a later real value takes the
            // node without a conflict, regardless of the presence block's priority
            var result = Merge(
                Block("a", """<SystemMonitor><Sleep grace="1s" /></SystemMonitor>""", priority: 5),
                Block("b", """<SystemMonitor><Sleep /></SystemMonitor>""", priority: 5),
                Block("c", """<SystemMonitor><Sleep>30min</Sleep></SystemMonitor>""", priority: 1));

            Assert.Equal("30min", Assert.Single(Elements(result, "Sleep")).Value);
        }

        [Fact]
        public void NamelessCollectionItems_AreAppended()
        {
            var result = Merge(
                Block("a", """<SystemMonitor><NetworkMonitor interface="en0" /></SystemMonitor>"""),
                Block("b", """<SystemMonitor><NetworkMonitor interface="en12" /></SystemMonitor>"""));

            var monitors = Elements(result, "NetworkMonitor");

            Assert.Equal(2, monitors.Count);
            Assert.Equal("en0", Attribute(monitors[0], "interface")); // document order preserved
            Assert.Equal("en12", Attribute(monitors[1], "interface"));
        }

        [Fact]
        public void NamelessSingletons_AreMerged()
        {
            var result = Merge(
                Block("a", """<SystemMonitor><DisplayMonitor preventIdle="true" /></SystemMonitor>"""),
                Block("b", """<SystemMonitor><DisplayMonitor disabled="true" /></SystemMonitor>"""));

            var display = Assert.Single(Elements(result, "DisplayMonitor"));

            Assert.Equal("true", Attribute(display, "preventIdle"));
            Assert.Equal("true", Attribute(display, "disabled"));
        }

        [Fact]
        public void MergesRecursively()
        {
            var result = Merge(
                Block("a", """<SystemMonitor><NetworkMonitor name="WiFi"><RemoteHost name="nas" /></NetworkMonitor></SystemMonitor>"""),
                Block("b", """<SystemMonitor><NetworkMonitor name="WiFi"><RemoteHost name="nas" onServiceDemand="knock" /><RemoteHost name="pc" /></NetworkMonitor></SystemMonitor>"""));

            var hosts = Elements(Assert.Single(Elements(result, "NetworkMonitor")), "RemoteHost");

            Assert.Equal(2, hosts.Count);
            Assert.Equal("knock", Attribute(hosts[0], "onServiceDemand"));
            Assert.Equal("pc", hosts[1].ItemName);
        }

        [Fact]
        public void TextContent_LaterBlockWins()
        {
            var result = Merge(
                Block("a", """<SystemMonitor><Process name="p">/old/path</Process></SystemMonitor>"""),
                Block("b", """<SystemMonitor><Process name="p">/new/path</Process></SystemMonitor>"""));

            Assert.Equal("/new/path", Assert.Single(Elements(result, "Process")).Value);
        }

        [Fact]
        public void HigherPriority_Supersedes_RegardlessOfOrder()
        {
            // an earlier higher-priority block keeps its value against later blocks
            var result = Merge(
                Block("a", """<SystemMonitor onDemand="sleepless" />""", priority: 1),
                Block("b", """<SystemMonitor onDemand="wake" />"""));

            Assert.Equal("sleepless", Attribute(result, "onDemand"));

            // and a later higher-priority block overrides an earlier one
            result = Merge(
                Block("a", """<SystemMonitor onDemand="sleepless" />"""),
                Block("b", """<SystemMonitor onDemand="wake" />""", priority: 1));

            Assert.Equal("wake", Attribute(result, "onDemand"));
        }

        [Fact]
        public void HigherPriority_SupersedesInNestedElements()
        {
            var result = Merge(
                Block("a", """<SystemMonitor><NetworkMonitor name="WiFi" network="10.1.0.0/16" /></SystemMonitor>""", priority: 2),
                Block("b", """<SystemMonitor><NetworkMonitor name="WiFi" network="10.2.0.0/16" interface="en0" /></SystemMonitor>"""));

            var monitor = Assert.Single(Elements(result, "NetworkMonitor"));

            Assert.Equal("10.1.0.0/16", Attribute(monitor, "network")); // kept from the higher-priority block
            Assert.Equal("en0", Attribute(monitor, "interface"));       // non-conflicting values still merge
        }

        [Fact]
        public void OnConflictFirst_KeepsTheEarlierValue()
        {
            var result = Merge(ConflictResolution.First,
                Block("a", """<SystemMonitor onDemand="sleepless" />"""),
                Block("b", """<SystemMonitor onDemand="wake" />"""));

            Assert.Equal("sleepless", Attribute(result, "onDemand"));
        }

        [Fact]
        public void OnConflictError_ThrowsOnEqualPriorityConflicts()
        {
            Assert.Throws<ConfigurationValueException>(() => Merge(ConflictResolution.Error,
                Block("a", """<SystemMonitor onDemand="sleepless" />"""),
                Block("b", """<SystemMonitor onDemand="wake" />""")));
        }

        [Fact]
        public void OnConflictError_AcceptsConflictsResolvedByPriority()
        {
            var result = Merge(ConflictResolution.Error,
                Block("a", """<SystemMonitor onDemand="sleepless" />"""),
                Block("b", """<SystemMonitor onDemand="wake" />""", priority: 1));

            Assert.Equal("wake", Attribute(result, "onDemand"));
        }

        [Fact]
        public void OnConflictError_AcceptsIdenticalValues()
        {
            var result = Merge(ConflictResolution.Error,
                Block("a", """<SystemMonitor onDemand="sleepless" />"""),
                Block("b", """<SystemMonitor onDemand="sleepless" />"""));

            Assert.Equal("sleepless", Attribute(result, "onDemand"));
        }

        [Fact]
        public void BareElementPresence_NeverConflictsWithARealValue()
        {
            // a bare <Sleep/> reasserts the node, it does not empty it - no conflict,
            // even under onConflict="error"
            var result = Merge(ConflictResolution.Error,
                Block("a", """<SystemMonitor><Sleep>5min</Sleep></SystemMonitor>"""),
                Block("b", """<SystemMonitor><Sleep /></SystemMonitor>"""));

            Assert.Equal("5min", Assert.Single(Elements(result, "Sleep")).Value);

            // and real content fills a bare node without a conflict
            result = Merge(ConflictResolution.Error,
                Block("a", """<SystemMonitor><Sleep /></SystemMonitor>"""),
                Block("b", """<SystemMonitor><Sleep>5min</Sleep></SystemMonitor>"""));

            Assert.Equal("5min", Assert.Single(Elements(result, "Sleep")).Value);
        }

        [Fact]
        public void NoActiveBlocks_YieldsBareSystemMonitor()
        {
            var result = Merge();

            Assert.Equal("SystemMonitor", result.Name);
            Assert.Empty(result.Children); // no attributes, no elements
        }
    }
}
