using MadWizard.Desomnia.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The XML migration helpers and the change tracking behind them: a helper records the
    /// note first (with the path as the file has it) and then changes the document with the
    /// tracking suspended; a change made past the helpers is recorded as a Warning while a
    /// step runs; a helper inside another helper's action takes no note of its own unless it
    /// is given an explicit reason.
    /// </summary>
    public class XMigrationExtensionsTests
    {
        private static readonly XNamespace Env = "environment:process";

        // a document as a step sees it: the tracker listens (the migrator does that around a step)
        private static (XDocument Document, XMigrationTracker Tracker) Tracked(string xml)
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

            var tracker = XMigrationTracker.For(document);

            tracker.Listen();

            return (document, tracker);
        }

        private static XElement Monitor(XDocument document) => document.Root!.Element("NetworkMonitor")!;

        #region Rename

        [Fact]
        internal void RenameAttribute_KeepsThePosition_ReturnsTheReplacement_AndNotesTheOldPath()
        {
            var (document, tracker) = Tracked("""<SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" other="x" /></SystemMonitor>""");

            var attribute = Monitor(document).Attribute("watchUDPPort")!;

            var renamed = attribute.MigrateRename("watchPort");

            Assert.Equal(["name", "watchPort", "other"], Monitor(document).Attributes().Select(a => a.Name.LocalName));
            Assert.Equal("9", renamed.Value);
            Assert.Same(Monitor(document), renamed.Parent);
            Assert.Null(attribute.Parent); // the original is detached

            var note = Assert.Single(tracker.Take());

            Assert.Equal(new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort", LogLevel.Information, "renamed to @watchPort"), note);
        }

        [Fact]
        internal void RenameAttribute_KeepsTheNamespace()
        {
            var (document, tracker) = Tracked("""<EnvironmentMonitor xmlns:env="environment:process"><Environment env:USER="kevin" /></EnvironmentMonitor>""");

            var environment = document.Root!.Element("Environment")!;

            var renamed = environment.Attribute(Env + "USER")!.MigrateRename("LOGIN");

            Assert.Equal(Env + "LOGIN", renamed.Name);
            Assert.Equal("kevin", environment.Attribute(Env + "LOGIN")!.Value);
            Assert.Equal("/EnvironmentMonitor/Environment/@env:USER", Assert.Single(tracker.Take()).Path);
        }

        [Fact]
        internal void RenameElement_KeepsTheNamespace_AndNotesTheOldPath()
        {
            var (document, tracker) = Tracked("""<SystemMonitor><NetworkMonitor name="Ethernet" /></SystemMonitor>""");

            Monitor(document).MigrateRename("NetworkWatch");

            Assert.NotNull(document.Root!.Element("NetworkWatch"));

            Assert.Equal(new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']", LogLevel.Information, "renamed to <NetworkWatch>"), Assert.Single(tracker.Take()));
        }

        #endregion

        #region Value, remove, add, replace, move

        [Fact]
        internal void SetValue_NotesOldAndNew()
        {
            var (document, tracker) = Tracked("""<SystemMonitor timeout="2min" />""");

            document.Root!.Attribute("timeout")!.MigrateSetValue("5min");

            Assert.Equal("5min", document.Root.Attribute("timeout")!.Value);
            Assert.Equal(new MigrationAnnotation("/SystemMonitor/@timeout", LogLevel.Information, "changed from \"2min\" to \"5min\""), Assert.Single(tracker.Take()));
        }

        [Fact]
        internal void Remove_IsAWarningByDefault()
        {
            var (document, tracker) = Tracked("""<SystemMonitor legacy="x"><Obsolete /></SystemMonitor>""");

            document.Root!.Attribute("legacy")!.MigrateRemove();
            document.Root.Element("Obsolete")!.MigrateRemove();

            Assert.Null(document.Root.Attribute("legacy"));
            Assert.Empty(document.Root.Elements());

            Assert.Equal([
                new MigrationAnnotation("/SystemMonitor/@legacy", LogLevel.Warning, "removed"),
                new MigrationAnnotation("/SystemMonitor/Obsolete", LogLevel.Warning, "removed"),
            ], tracker.Take());
        }

        [Fact]
        internal void Add_NotesThePathTheNodeGot()
        {
            var (document, tracker) = Tracked("""<SystemMonitor><NetworkMonitor name="Ethernet" /></SystemMonitor>""");

            Monitor(document).MigrateAdd(new XAttribute("watchPort", "9"));
            Monitor(document).MigrateAdd(new XElement("Service", new XAttribute("name", "SSH")));

            Assert.Equal("9", Monitor(document).Attribute("watchPort")!.Value);
            Assert.NotNull(Monitor(document).Element("Service"));

            Assert.Equal([
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchPort", LogLevel.Information, "added @watchPort = \"9\""),
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/Service[@name='SSH']", LogLevel.Information, "added <Service>"),
            ], tracker.Take());
        }

        [Fact]
        internal void Add_RefusesAnAttachedNode()
        {
            var (document, _) = Tracked("""<SystemMonitor a="1"><NetworkMonitor name="Ethernet" /></SystemMonitor>""");

            Assert.Throws<ArgumentException>(() => Monitor(document).MigrateAdd(document.Root!.Attribute("a")!));
            Assert.Throws<ArgumentException>(() => document.Root!.MigrateAdd(Monitor(document)));
        }

        [Fact]
        internal void Replace_And_Move_KeepTheObjects()
        {
            var (document, tracker) = Tracked("""<SystemMonitor timeout="2min"><NetworkMonitor name="Ethernet"><Old /></NetworkMonitor><Other /></SystemMonitor>""");

            var timeout = document.Root!.Attribute("timeout")!;
            var other = document.Root.Element("Other")!;

            Monitor(document).Element("Old")!.MigrateReplace(new XElement("New"));
            timeout.MigrateMove(Monitor(document));
            other.MigrateMove(Monitor(document));

            Assert.Null(document.Root.Attribute("timeout"));
            Assert.Same(timeout, Monitor(document).Attribute("timeout")); // the same object, attached anew
            Assert.Same(other, Monitor(document).Element("Other"));
            Assert.NotNull(Monitor(document).Element("New"));

            Assert.Equal([
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/Old", LogLevel.Information, "replaced by <New>"),
                new MigrationAnnotation("/SystemMonitor/@timeout", LogLevel.Information, "moved to /SystemMonitor/NetworkMonitor[@name='Ethernet']"),
                new MigrationAnnotation("/SystemMonitor/Other", LogLevel.Information, "moved to /SystemMonitor/NetworkMonitor[@name='Ethernet']"),
            ], tracker.Take());
        }

        [Fact]
        internal void Reason_IsAppended_AndLevelIsTakenAsGiven()
        {
            var (document, tracker) = Tracked("""<SystemMonitor onDemand="start+5s" />""");

            document.Root!.Attribute("onDemand")!.MigrateSetValue("start", LogLevel.Warning, "schedule +5s removed");

            Assert.Equal(new MigrationAnnotation("/SystemMonitor/@onDemand", LogLevel.Warning, "changed from \"start+5s\" to \"start\" - schedule +5s removed"), Assert.Single(tracker.Take()));
        }

        #endregion

        #region The general seam and nesting

        [Fact]
        internal void Migrate_RecordsTheReason_AndRunsTheActionUntracked()
        {
            var (document, tracker) = Tracked("""<SystemMonitor><NetworkMonitor name="Ethernet" a="1" b="2" /></SystemMonitor>""");

            Monitor(document).Migrate(LogLevel.Information, "restructured", monitor =>
            {
                monitor.SetAttributeValue("a", null);
                monitor.SetAttributeValue("c", "3");
                monitor.Name = "NetworkWatch";
            });

            Assert.Equal(new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']", LogLevel.Information, "restructured"), Assert.Single(tracker.Take()));
        }

        [Fact]
        internal void NestedHelper_TakesNoNote_UnlessGivenAReason()
        {
            var (document, tracker) = Tracked("""<SystemMonitor><NetworkMonitor name="Ethernet" a="1" b="2" /></SystemMonitor>""");

            Monitor(document).Migrate(LogLevel.Information, "outer", monitor =>
            {
                monitor.Attribute("a")!.MigrateRename("x");                            // implicit: a detail of the outer operation
                monitor.Attribute("b")!.MigrateSetValue("3", reason: "explicit");     // explicit: recorded
                monitor.AnnotateMigration(LogLevel.Warning, "look here");             // a pure note is always explicit
                monitor.SetAttributeValue("raw", "1");                                // no auto note inside a helper
            });

            Assert.Equal([
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']", LogLevel.Information, "outer"),
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/@b", LogLevel.Information, "changed from \"2\" to \"3\" - explicit"),
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']", LogLevel.Warning, "look here"),
            ], tracker.Take());

            Assert.Equal(["name", "x", "b", "raw"], Monitor(document).Attributes().Select(a => a.Name.LocalName));
        }

        [Fact]
        internal void SilentChange_ThroughLogLevelNone_IsRecordedNotWarned()
        {
            var (document, tracker) = Tracked("""<SystemMonitor a="1" />""");

            document.Root!.Attribute("a")!.MigrateSetValue("1", LogLevel.None);

            Assert.Equal(LogLevel.None, Assert.Single(tracker.Take()).Level);
        }

        [Fact]
        internal void DetachedNode_CannotBeMigrated()
        {
            var detached = new XElement("Loose", new XAttribute("a", "1"));

            Assert.Throws<InvalidOperationException>(() => detached.MigrateRename("x"));
            Assert.Throws<InvalidOperationException>(() => detached.Attribute("a")!.MigrateSetValue("2"));
            Assert.Throws<InvalidOperationException>(() => detached.AnnotateMigration(LogLevel.Information, "x"));
        }

        #endregion

        #region Tracking

        [Fact]
        internal void RawChanges_AreWarnings_WhileTheTrackerListens_AndSilentAfterwards()
        {
            var (document, tracker) = Tracked("""<SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9"><Service name="SSH" /></NetworkMonitor></SystemMonitor>""");

            Monitor(document).Attribute("watchUDPPort")!.Value = "10";
            Monitor(document).Element("Service")!.Remove();
            Monitor(document).Add(new XElement("Host", new XAttribute("name", "nas")));
            Monitor(document).Name = "NetMon";

            Assert.Equal([
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort", LogLevel.Warning, "changed from \"9\" to \"10\" outside a migration helper"),
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/Service[@name='SSH']", LogLevel.Warning, "removed outside a migration helper"),
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']/Host[@name='nas']", LogLevel.Warning, "added outside a migration helper"),
                new MigrationAnnotation("/SystemMonitor/NetworkMonitor[@name='Ethernet']", LogLevel.Warning, "renamed to <NetMon> outside a migration helper"),
            ], tracker.Take());

            tracker.Unlisten();

            document.Root!.SetAttributeValue("later", "x");

            Assert.Empty(tracker.Take());
        }

        [Fact]
        internal void HelpersWork_WithoutAListeningTracker()
        {
            // outside a run (unit tests, tooling): the notes are still recorded, nothing is tracked
            var document = XDocument.Parse("""<SystemMonitor a="1" />""");

            document.Root!.Attribute("a")!.MigrateRename("b");
            document.Root.SetAttributeValue("raw", "1");

            Assert.Equal("renamed to @b", Assert.Single(document.TakeMigrationAnnotations()).Message);
        }

        [Fact]
        internal void Release_RemovesTheTracker()
        {
            var (document, tracker) = Tracked("""<SystemMonitor />""");

            tracker.Release();

            Assert.Null(XMigrationTracker.Of(document));

            document.Root!.SetAttributeValue("later", "x");

            Assert.Empty(document.TakeMigrationAnnotations());
        }

        #endregion

        #region Queries

        [Fact]
        internal void Queries_MatchLocalNames_CaseInsensitively_OutsideNamespaces()
        {
            var document = XDocument.Parse("""
                <EnvironmentMonitor xmlns:env="environment:process" Version="1">
                  <Environment env:name="not-a-setting" NAME="a"><SystemMonitor><networkmonitor name="x" /></SystemMonitor></Environment>
                  <DefaultEnvironment><NetworkMonitor name="y" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var root = document.Root!;
            var environment = root.Element("Environment")!;

            Assert.Equal("1", root.AttributeNamed("version")!.Value);
            Assert.Equal("a", environment.AttributeNamed("name")!.Value); // the namespaced env:name is a condition, not a setting
            Assert.Null(root.AttributeNamed("missing"));

            Assert.Equal(["x", "y"], document.DescendantsNamed("NETWORKMONITOR").Select(e => e.Attribute("name")!.Value));
            Assert.Single(root.ElementsNamed("defaultenvironment"));
            Assert.Empty(root.ElementsNamed("NetworkMonitor")); // direct children only
            Assert.True(root.HasLocalName("environmentmonitor"));
        }

        #endregion
    }
}
