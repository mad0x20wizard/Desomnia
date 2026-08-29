using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The configuration format pipeline: the version algebra over the registered modules, the
    /// <c>&lt;?config?&gt;</c> header (version declaration, legacy root attribute, migration
    /// settings), the step-wise migration with its report channel (annotations, log lines,
    /// XPaths), the file write-back and the own-write / cache behaviour.
    /// </summary>
    public class ConfigurationMigratorTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;
        private readonly string _path;

        public ConfigurationMigratorTests() => _path = Path.Combine(_directory, "monitor.xml");

        public void Dispose()
        {
            // a test may leave the directory read-only
            var info = new DirectoryInfo(_directory) { Attributes = FileAttributes.Normal };

            foreach (var file in info.GetFiles())
                file.Attributes = FileAttributes.Normal;

            Directory.Delete(_directory, recursive: true);
        }

        private void WriteConfig(string content) => File.WriteAllText(_path, content);

        #region Harness

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

            public IEnumerable<string> Messages => Entries.Select(entry => entry.Message);

            public IEnumerable<string> At(LogLevel level) => Entries.Where(entry => entry.Level == level).Select(entry => entry.Message);

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception), exception));
        }

        private abstract class FakeModule : ConfigurableModule, IXConfigurationMigration
        {
            public uint Min { get; init; } = 1;

            // "accepts everything" - the real default (LATEST_VERSION) would limit the simulated
            // future formats of these tests to the real product version
            public uint Max { get; init; } = uint.MaxValue;

            public Action<XDocument, uint>? Migrate { get; init; }

            public List<(uint Version, XDocument Document)> Calls { get; } = [];

            protected internal override uint MinVersion => Min;

            protected internal override uint MaxVersion => Max;

            void IXConfigurationMigration.Run(XDocument configuration, uint version)
            {
                Calls.Add((version, configuration));

                Migrate?.Invoke(configuration, version);
            }
        }

        // distinct types: the migrator names modules by their type
        private sealed class ModuleA : FakeModule;
        private sealed class ModuleB : FakeModule;
        private sealed class ModuleC : FakeModule;

        private CapturingLogger _log = new();

        // the migration protocol is dated with the current date
        private static string Today => DateTime.Now.ToString("dd.MM.yyyy");

        private XConfigurationMigrator CreateMigrator(uint latest, params ConfigurableModule[] modules)
        {
            var migrator = new XConfigurationMigrator(() => _path) { LatestVersion = latest, Logger = _log = new CapturingLogger() };

            //migrator.Registry.Logger = _log; // engine and registry log distinct categories; the tests capture both

            foreach (var module in modules)
                migrator.Register(module);

            return migrator;
        }

        // the typical v1 -> v2 rename step, through the helper: it records the note (with the path
        // as the file has it) and carries out the change, so the tracker sees nothing to report
        private static void RenameWatchPort(XDocument document, uint version)
        {
            foreach (var attribute in document.Descendants("NetworkMonitor").Attributes("watchUDPPort").ToList())
                attribute.MigrateRename("watchPort");
        }

        // the version as the root element's attribute declares it (the authoritative place, the stamp goes there)
        private static string Version(XDocument document)
            => document.Root!.Attributes().Single(a => a.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase)).Value;

        // the version as the <?config?> header declares it (kept in sync where the user wrote one)
        private static string HeaderVersion(XDocument document)
            => Regex.Match(Header(document).Data, """version\s*=\s*(?:"([^"]*)"|'([^']*)')""", RegexOptions.IgnoreCase) is { Success: true } match
                ? match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value
                : throw new InvalidOperationException("the header declares no version");

        private static XProcessingInstruction Header(XDocument document)
            => document.Nodes().OfType<XProcessingInstruction>().Single(pi => pi.Target.Equals("config", StringComparison.OrdinalIgnoreCase));

        private static bool HasHeader(XDocument document)
            => document.Nodes().OfType<XProcessingInstruction>().Any(pi => pi.Target.Equals("config", StringComparison.OrdinalIgnoreCase));

        #endregion

        #region Version algebra

        [Fact]
        internal void Algebra_EmptyModuleSet_SupportsLatest_RequiresOne()
        {
            var migrator = CreateMigrator(latest: 3);

            Assert.Equal(3u, migrator.SupportedVersion);
            Assert.Equal(1u, migrator.RequiredVersion);
        }

        [Fact]
        internal void Algebra_TakesTheMinOfMax_AndTheMaxOfMin()
        {
            var migrator = CreateMigrator(latest: 5, new ModuleA { Min = 2, Max = 4 }, new ModuleB { Min = 3 }, new ModuleC());

            Assert.Equal(4u, migrator.SupportedVersion);
            Assert.Equal(3u, migrator.RequiredVersion);
        }

        [Fact]
        internal void Algebra_StrictModule_IsWarnedAbout_Once()
        {
            var migrator = CreateMigrator(latest: 3, new ModuleA { Max = 2 }, new ModuleB());

            _ = migrator.SupportedVersion;
            _ = migrator.RequiredVersion;

            Assert.Equal(["ModuleA limits the configuration format to version 2 (current: 3)."], _log.At(LogLevel.Warning));
        }

        [Fact]
        internal void Algebra_RequiredAboveSupported_Throws_NamingBoth()
        {
            var migrator = CreateMigrator(latest: 3, new ModuleA { Min = 3 }, new ModuleB { Max = 2 });

            var ex = Assert.Throws<ConfigurationMigrationException>(() => migrator.SupportedVersion);

            Assert.Equal("The configuration format cannot be satisfied: ModuleA requires version 3, but ModuleB supports at most version 2.", ex.Message);

            // and the same at the first read of a file
            Assert.Throws<ConfigurationMigrationException>(() => migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />"""));
        }

        [Fact]
        internal void Register_AfterTheFirstRead_Throws()
        {
            var migrator = CreateMigrator(latest: 1);

            migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");

            Assert.Throws<InvalidOperationException>(() => migrator.Register(new ModuleA()));
        }

        #endregion

        #region Version declaration

        [Theory]
        [InlineData("""<?config version="abc" autoMigrate="transient"?><SystemMonitor />""", "Invalid <?config version=\"abc\"?>; expected a positive integer.")]
        [InlineData("""<?config version="0" autoMigrate="transient"?><SystemMonitor />""", "Invalid <?config version=\"0\"?>; expected a positive integer.")]
        [InlineData("""<?config version="-1" autoMigrate="transient"?><SystemMonitor />""", "Invalid <?config version=\"-1\"?>; expected a positive integer.")]
        [InlineData("""<?config version="" autoMigrate="transient"?><SystemMonitor />""", "Invalid <?config version=\"\"?>; expected a positive integer.")]
        [InlineData("""<SystemMonitor version="abc" />""", "Invalid <SystemMonitor version=\"abc\">; expected a positive integer.")]
        [InlineData("""<SystemMonitor version="0" />""", "Invalid <SystemMonitor version=\"0\">; expected a positive integer.")]
        internal void Version_Invalid_Throws(string xml, string message)
        {
            var migrator = CreateMigrator(latest: 2);

            var ex = Assert.Throws<ConfigurationValueException>(() => migrator.Read(xml));

            Assert.Equal(message, ex.Message);
        }

        [Theory]
        [InlineData("""<SystemMonitor version="2" />""")] // the authoritative place: the root element
        [InlineData("""<?config version="2"?><SystemMonitor />""")] // header style is fine too
        [InlineData("""<?config version="2"?><SystemMonitor version="2" />""")] // both, matching
        internal void Version_MayBeDeclaredOnTheRoot_InTheHeader_OrBoth(string xml)
        {
            var module = new ModuleA { Min = 2 };

            var file = CreateMigrator(latest: 2, module).Read(xml);

            Assert.Equal(2u, file.Version);
            Assert.Empty(module.Calls); // current: nothing to migrate
        }

        [Fact]
        internal void Version_DeclaredInBothPlaces_MustMatch()
        {
            var ex = Assert.Throws<ConfigurationValueException>(() =>
                CreateMigrator(latest: 2).Read("""<?config version="2"?><SystemMonitor version="1" />"""));

            Assert.Equal("<SystemMonitor> declares version=\"1\" while <?config version=\"2\"?> declares version 2; " +
                "the two declarations must match.", ex.Message);
        }

        [Theory]
        [InlineData("""<SystemMonitor />""")]
        [InlineData("""<?xml version="1.0"?><EnvironmentMonitor />""")]
        [InlineData("""<!-- no header --><SystemMonitor />""")]
        internal void Version_NoHeader_IsVersionOne_MigratedInMemory(string xml)
        {
            var a = new ModuleA { Min = 2 };

            var file = CreateMigrator(latest: 2, a).Read(xml);

            Assert.Equal([2u], a.Calls.Select(call => call.Version));
            Assert.Contains("Migrating the configuration from version 1 -> 2...", _log.At(LogLevel.Warning));

            // stamped as the root element's attribute - no header is invented for it
            Assert.Equal("2", Version(file.Root.Document!));
            Assert.False(HasHeader(file.Root.Document!));
        }

        [Fact]
        internal void Version_NoHeader_IsTransient_NeverWritten()
        {
            WriteConfig("""<SystemMonitor />""");

            CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path));

            Assert.Equal("<SystemMonitor />", File.ReadAllText(_path));
        }

        [Fact]
        internal void Version_RootAttribute_IsStampedInPlace_WhateverItsCasing()
        {
            WriteConfig("<?xml version=\"1.0\"?>\r\n<SystemMonitor timeout=\"1min\" VERSION=\"1\" marker=\"x\" />\r\n");

            var a = new ModuleA { Min = 2 };

            var file = CreateMigrator(latest: 2, a).Read(File.ReadAllText(_path));

            Assert.Equal([2u], a.Calls.Select(call => call.Version));

            // the attribute keeps its place and casing, only the value changes
            Assert.Equal(["timeout", "VERSION", "marker"], file.Root.Attributes().Select(attribute => attribute.Name.LocalName));
            Assert.Equal("2", Version(file.Root.Document!));

            // transient: the file still carries the old declaration
            Assert.Contains("VERSION=\"1\"", File.ReadAllText(_path));
        }

        [Fact]
        internal void Version_WrittenBack_StampsTheRootAttribute_AndAHeaderThatDeclaresOne()
        {
            // a file declaring the version in both places: both are stamped, the style survives
            WriteConfig("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<?config version=\"1\" autoMigrate=\"persistent\"?>\r\n<SystemMonitor version=\"1\">\r\n</SystemMonitor>\r\n");

            CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path));

            Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<?config version=\"2\" autoMigrate=\"persistent\"?>\r\n" +
                $"<!-- MIGRATION PROTOCOL ({Today})\r\n  Version 1 -> 2.\r\n-->\r\n" +
                "<SystemMonitor version=\"2\">\r\n</SystemMonitor>\r\n", File.ReadAllText(_path));
        }

        [Fact]
        internal void Version_WrittenBack_AHeaderWithoutVersion_StaysWithoutOne()
        {
            // the header only speaks about migration; the version lives on the root - and stays there
            WriteConfig("<?config autoMigrate=\"persistent\"?>\r\n<SystemMonitor version=\"1\">\r\n</SystemMonitor>\r\n");

            CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path));

            Assert.Equal("<?config autoMigrate=\"persistent\"?>\r\n" +
                $"<!-- MIGRATION PROTOCOL ({Today})\r\n  Version 1 -> 2.\r\n-->\r\n" +
                "<SystemMonitor version=\"2\">\r\n</SystemMonitor>\r\n", File.ReadAllText(_path));
        }

        [Fact]
        internal void Version_NewerThanSupported_PassesTheMigratorQuietly_TheCheckRefusesIt()
        {
            var migrator = CreateMigrator(latest: 2);

            // the migrator has no business with a too-new file (nothing to migrate) ...
            var file = migrator.Read("""<?config version="3" autoMigrate="transient"?><SystemMonitor />""");

            Assert.Equal(3u, file.Version);
            Assert.Empty(_log.Entries);

            // ... the version check is the layer that refuses it - with or without a migrator
            var ex = Assert.Throws<ConfigurationMigrationException>(() => migrator.Registry.Validate(file.Version));

            Assert.Equal("The configuration file uses format version 3, but this build supports at most version 2. " +
                "The file seems to belong to a newer version of the software.", ex.Message);
        }

        [Fact]
        internal void Version_LimitedByAModule_IsTheSupportedOne()
        {
            var migrator = CreateMigrator(latest: 3, new ModuleA { Max = 2 });

            Assert.Throws<ConfigurationMigrationException>(() => migrator.Registry.Validate(3));

            migrator.Registry.Validate(2); // fine
        }

        [Fact]
        internal void Version_OlderThanLatest_IsFine_WhenNoModuleDemandsMore()
        {
            // a v1 file on a v2 build: perfectly valid as long as no loaded module requires v2
            var module = new ModuleA { Min = 1 };

            var migrator = CreateMigrator(latest: 2, module);

            var file = migrator.Read("""<SystemMonitor />""");

            migrator.Registry.Validate(file.Version);

            Assert.Equal(1u, file.Version);
            Assert.Empty(module.Calls); // and nothing was migrated for it
            Assert.Empty(_log.Entries);
        }

        [Fact]
        internal void Version_OlderThanRequired_AndNotMigrated_IsRefusedByTheCheck()
        {
            // autoMigrate="never": the migrator stays away, the version check names the demanding module
            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2 });

            var file = migrator.Read("""<?config autoMigrate="never"?><SystemMonitor version="1" />""");

            Assert.Equal(1u, file.Version);

            var ex = Assert.Throws<ConfigurationMigrationException>(() => migrator.Registry.Validate(file.Version));

            Assert.Equal("The configuration file uses format version 1, but ModuleA requires at least version 2. " +
                "Update the configuration file manually, or declare autoMigrate=\"transient\" or \"persistent\" " +
                "in the <?config?> header to have it migrated automatically.", ex.Message);
        }

        [Fact]
        internal void Version_Current_CallsNoSeam_LogsNothing()
        {
            var module = new ModuleA { Min = 2 };

            var migrator = CreateMigrator(latest: 2, module);

            var file = migrator.Read("""<?config version="2" autoMigrate="transient"?><SystemMonitor timeout="1min" />""");

            Assert.Empty(module.Calls);
            Assert.Empty(_log.Entries);
            Assert.Equal("SystemMonitor", file.RootName);
        }

        [Fact]
        internal void Version_Header_IsMatchedCaseInsensitively_AndTheStampKeepsTheRestAsWritten()
        {
            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2 });

            var file = migrator.Read("""<?CONFIG  AutoMigrate='transient'   VERSION = '1'  writeBackupXML="b?.xml"?><SystemMonitor />""");

            Assert.Equal("CONFIG", Header(file.Root.Document!).Target);
            Assert.Equal("AutoMigrate='transient'   VERSION = '2'  writeBackupXML=\"b?.xml\"", Header(file.Root.Document!).Data);

            // ... and the root element received the authoritative stamp alongside
            Assert.Equal("2", Version(file.Root.Document!));
        }

        [Theory]
        [InlineData("""<?system a="1"?><?config version="1"?><SystemMonitor />""")] // after another instruction
        [InlineData("""<SystemMonitor /><?config version="1"?>""")] // after the root
        internal void Version_Header_MustBeTheFirstInstruction(string xml)
        {
            var ex = Assert.Throws<ConfigurationValueException>(() => CreateMigrator(latest: 1).Read(xml));

            Assert.Equal("<?config?> must be the first processing instruction of the configuration file, right after the XML declaration.", ex.Message);
        }

        [Fact]
        internal void Version_Header_MayFollowComments_AndTheDeclaration()
        {
            var file = CreateMigrator(latest: 1).Read("""<?xml version="1.0"?><!-- c --><?config version="1"?><!-- d --><?system a="1"?><SystemMonitor />""");

            Assert.Equal(1u, file.Version);
            Assert.Equal("1", HeaderVersion(file.Root.Document!));
            Assert.Equal(["a"], file.SystemDirectives.Select(directive => directive.Key));
        }

        #endregion

        #region Step sequencing

        [Fact]
        internal void Steps_CallTheModulesWhoseMinVersionReachesTheStep_InRegistrationOrder()
        {
            List<string> trace = [];

            var a = new ModuleA { Min = 3, Migrate = (_, v) => trace.Add($"A{v}") };
            var b = new ModuleB { Min = 2, Migrate = (_, v) => trace.Add($"B{v}") };
            var c = new ModuleC { Min = 1, Migrate = (_, v) => trace.Add($"C{v}") };

            var migrator = CreateMigrator(latest: 3, a, b, c);

            var file = migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");

            Assert.Equal(["A2", "B2", "A3"], trace);
            Assert.Empty(c.Calls);

            // the same document instance travels through all steps and is what the reader wraps
            Assert.Same(a.Calls[0].Document, a.Calls[1].Document);
            Assert.Same(a.Calls[0].Document, b.Calls[0].Document);
            Assert.Same(a.Calls[0].Document, file.Root.Document);

            Assert.Equal("3", Version(file.Root.Document!));
        }

        [Fact]
        internal void Steps_SeeTheVersionStampedByThePreviousStep()
        {
            List<string> seen = [];

            var a = new ModuleA { Min = 3, Migrate = (d, v) => seen.Add($"{v}:{HeaderVersion(d)}") };

            CreateMigrator(latest: 3, a).Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");

            Assert.Equal(["2:1", "3:2"], seen);
        }

        #endregion

        #region Logging

        [Fact]
        internal void Logging_OneWarningPerStep_ThenTheNotesAtTheirLevels()
        {
            var a = new ModuleA
            {
                Min = 3,
                Migrate = (d, v) =>
                {
                    if (v == 2)
                    {
                        d.Root!.AnnotateMigration(LogLevel.Information, "info note");
                        d.Root!.AnnotateMigration(LogLevel.None, "silent note");
                    }
                    else
                    {
                        d.Root!.AnnotateMigration(LogLevel.Warning, "warn note");
                    }
                },
            };

            CreateMigrator(latest: 3, a).Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");

            Assert.Equal(
            [
                (LogLevel.Warning, "Migrating the configuration from version 1 -> 2..."),
                (LogLevel.Information, "/SystemMonitor -> info note"),
                (LogLevel.Warning, "Migrating the configuration from version 2 -> 3..."),
                (LogLevel.Warning, "/SystemMonitor -> warn note"),
            ], _log.Entries.Select(entry => (entry.Level, entry.Message)));
        }

        #endregion

        #region XPath capture

        [Fact]
        internal void XPath_IsCapturedAtAnnotationTime_BeforeTheChange()
        {
            var a = new ModuleA { Min = 2, Migrate = RenameWatchPort };

            var file = CreateMigrator(latest: 2, a).Read("""
                <?config version="1" autoMigrate="transient"?>
                <EnvironmentMonitor>
                  <DefaultEnvironment>
                    <SystemMonitor>
                      <NetworkMonitor name="Ethernet" watchUDPPort="9" />
                    </SystemMonitor>
                  </DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            Assert.Contains("/EnvironmentMonitor/DefaultEnvironment/SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort -> renamed to @watchPort",
                _log.At(LogLevel.Information));

            Assert.Equal("9", file.Root.Descendants("NetworkMonitor").Single().Attribute("watchPort")!.Value);
        }

        [Fact]
        internal void XPath_Describes_Names_Positions_Attributes_Prefixes_AndDocumentLevelNodes()
        {
            XNamespace env = "environment:process";

            var document = XDocument.Parse("""
                <?xml version="1.0"?>
                <!-- first -->
                <?config version="1"?>
                <?system a="1" ?>
                <!-- second -->
                <EnvironmentMonitor xmlns:env="environment:process" debounce="1s">
                  <Environment env:USER="kevin"><SystemMonitor /></Environment>
                  <Environment name="it's"><SystemMonitor><Item /><Item /></SystemMonitor></Environment>
                  <DefaultEnvironment>text<!-- inner --></DefaultEnvironment>
                </EnvironmentMonitor>
                <?system b="2" ?>
                """, LoadOptions.PreserveWhitespace);

            var root = document.Root!;
            var environments = root.Elements("Environment").ToList();
            var items = root.Descendants("Item").ToList();

            Assert.Equal("/", XPathDescriber.Describe(document));
            Assert.Equal("/EnvironmentMonitor", XPathDescriber.Describe(root));
            Assert.Equal("/EnvironmentMonitor/@debounce", XPathDescriber.Describe(root.Attribute("debounce")!));
            Assert.Equal("/EnvironmentMonitor/@xmlns:env", XPathDescriber.Describe(root.Attribute(XNamespace.Xmlns + "env")!));
            Assert.Equal("/EnvironmentMonitor/Environment[1]", XPathDescriber.Describe(environments[0]));
            Assert.Equal("/EnvironmentMonitor/Environment[1]/@env:USER", XPathDescriber.Describe(environments[0].Attribute(env + "USER")!));
            Assert.Equal("/EnvironmentMonitor/Environment[@name=\"it's\"]", XPathDescriber.Describe(environments[1]));
            Assert.Equal("/EnvironmentMonitor/Environment[@name=\"it's\"]/SystemMonitor/Item[2]", XPathDescriber.Describe(items[1]));
            Assert.Equal("/EnvironmentMonitor/DefaultEnvironment/text()", XPathDescriber.Describe(root.Element("DefaultEnvironment")!.Nodes().OfType<XText>().Single()));
            Assert.Equal("/EnvironmentMonitor/DefaultEnvironment/comment()", XPathDescriber.Describe(root.Element("DefaultEnvironment")!.Nodes().OfType<XComment>().Single()));

            var comments = document.Nodes().OfType<XComment>().ToList();
            var instructions = document.Nodes().OfType<XProcessingInstruction>().ToList();

            Assert.Equal("/comment()[1]", XPathDescriber.Describe(comments[0]));
            Assert.Equal("/comment()[2]", XPathDescriber.Describe(comments[1]));
            Assert.Equal("/processing-instruction('config')", XPathDescriber.Describe(instructions[0]));
            Assert.Equal("/processing-instruction('system')[1]", XPathDescriber.Describe(instructions[1]));
            Assert.Equal("/processing-instruction('system')[2]", XPathDescriber.Describe(instructions[2]));
        }

        [Fact]
        internal void XPath_PrefixedElement_UsesThePrefix()
        {
            var document = XDocument.Parse("""<Root xmlns:x="urn:x"><x:Child /></Root>""");

            Assert.Equal("/Root/x:Child", XPathDescriber.Describe(document.Root!.Elements().Single()));
        }

        [Fact]
        internal void XPath_MatchesTheNameAttributeCaseInsensitively_LikeTheBinder()
        {
            // <X Name="..."> is a named collection item to the binder, so it is one here, too
            var document = XDocument.Parse("""<Root><Item Name="a" /><Item NAME="b" /><Item /></Root>""");

            var items = document.Root!.Elements().ToList();

            Assert.Equal("/Root/Item[@Name='a']", XPathDescriber.Describe(items[0]));
            Assert.Equal("/Root/Item[@NAME='b']", XPathDescriber.Describe(items[1]));
            Assert.Equal("/Root/Item[3]", XPathDescriber.Describe(items[2]));
        }

        [Fact]
        internal void Annotate_DetachedNode_Throws_AndTheMigratorNamesTheModule()
        {
            var detached = new XElement("Loose");

            Assert.Throws<InvalidOperationException>(() => detached.AnnotateMigration(LogLevel.Information, "x"));

            var a = new ModuleA
            {
                Min = 2,
                Migrate = (d, _) =>
                {
                    var element = d.Root!.Element("Child")!;
                    element.Remove();
                    element.AnnotateMigration(LogLevel.Information, "too late");
                },
            };

            var ex = Assert.Throws<ConfigurationMigrationException>(() =>
                CreateMigrator(latest: 2, a).Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor><Child /></SystemMonitor>"""));

            Assert.StartsWith("ModuleA failed to migrate the configuration from version 1 -> 2:", ex.Message);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        #endregion

        #region Errors

        [Fact]
        internal void ErrorNote_IsLogged_ThenThrows()
        {
            var a = new ModuleA
            {
                Min = 2,
                Migrate = (d, _) =>
                {
                    d.Root!.AnnotateMigration(LogLevel.Error, "cannot map this");
                    d.Root!.AnnotateMigration(LogLevel.Critical, "nor this");
                    d.Root!.AnnotateMigration(LogLevel.Information, "but this was fine");
                },
            };

            var ex = Assert.Throws<ConfigurationMigrationException>(() =>
                CreateMigrator(latest: 2, a).Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />"""));

            Assert.Equal("The configuration cannot be migrated automatically from version 1 to version 2; 2 problem(s) need manual attention (see the log).", ex.Message);

            Assert.Contains("/SystemMonitor -> cannot map this", _log.At(LogLevel.Error));
            Assert.Contains("/SystemMonitor -> nor this", _log.At(LogLevel.Critical));
            Assert.Contains("/SystemMonitor -> but this was fine", _log.At(LogLevel.Information));
        }

        [Fact]
        internal void ConfigurationValueException_FromAModule_PassesThrough()
        {
            var a = new ModuleA { Min = 2, Migrate = (_, _) => throw new ConfigurationValueException("bad value") };

            var ex = Assert.Throws<ConfigurationValueException>(() =>
                CreateMigrator(latest: 2, a).Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />"""));

            Assert.Equal("bad value", ex.Message);
        }

        [Fact]
        internal void OtherException_FromAModule_IsWrapped()
        {
            var a = new ModuleA { Min = 2, Migrate = (_, _) => throw new NotSupportedException("boom") };

            var ex = Assert.Throws<ConfigurationMigrationException>(() =>
                CreateMigrator(latest: 2, a).Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />"""));

            Assert.Equal("ModuleA failed to migrate the configuration from version 1 -> 2: boom", ex.Message);
            Assert.IsType<NotSupportedException>(ex.InnerException);
        }

        [Fact]
        internal void FailedRead_IsNotCached()
        {
            int calls = 0;

            var a = new ModuleA { Min = 2, Migrate = (_, _) => { if (++calls == 1) throw new NotSupportedException("boom"); } };

            var migrator = CreateMigrator(latest: 2, a);

            Assert.Throws<ConfigurationMigrationException>(() => migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />"""));

            migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />"""); // the second attempt runs the module again

            Assert.Equal(2, calls);
        }

        #endregion

        #region <?config?> header

        [Fact]
        internal void Header_IsReadOffTheDocument_CaseInsensitively()
        {
            WriteConfig("""<?CONFIG Version="1" AutoMigrate='transient|persistent' WRITEBACKUPXML="monitor_v?.xml" ?><SystemMonitor />""");

            var document = new XMigrationDocument(XmlConfigurationReader.Parse(File.ReadAllText(_path)), "", () => _path, NullLogger.Instance);

            Assert.Equal(1u, document.Version);
            Assert.Equal(MigrationOption.Transient | MigrationOption.Persistent, document.Settings.Option);
            Assert.Equal("monitor_v?.xml", document.Settings.BackupPattern);
        }

        [Fact]
        internal void Header_Absent_MeansTheDefaults_VersionOneAndTransient()
        {
            foreach (var xml in new[] { """<SystemMonitor />""", """<?xml version="1.0"?><!-- c --><SystemMonitor />""", """<?system a="1"?><SystemMonitor />""" })
            {
                var document = new XMigrationDocument(XDocument.Parse(xml), "", () => null, NullLogger.Instance);

                Assert.Equal(1u, document.Version);
                Assert.Equal(MigrationSettings.Default, document.Settings);
                Assert.Equal(MigrationOption.Transient, document.Settings.Option);
            }
        }

        [Fact]
        internal void Header_WithoutAutoMigrate_KeepsTheDefault_Transient()
        {
            // the header only needs writing to FORBID the migration or make it persistent -
            // one that stays silent about it keeps the default
            var document = new XMigrationDocument(XDocument.Parse("""<?config version="1"?><SystemMonitor />"""), "", () => null, NullLogger.Instance);

            Assert.Equal(MigrationOption.Transient, document.Settings.Option);
            Assert.Null(document.Settings.BackupPattern);
        }

        [Theory]
        [InlineData("""<?config version="1" autoMigrate="transient"?><SystemMonitor><?config version="1" autoMigrate="persistent"?></SystemMonitor>""", "must be placed outside of the <SystemMonitor> root element")]
        [InlineData("""<?config version="1" autoMigrate="persistent"?><?config version="1" autoMigrate="never"?><SystemMonitor />""", "Only one <?config?> header is allowed")]
        [InlineData("""<?config version="1" autoMigrate=file?><SystemMonitor />""", "Invalid processing instruction <?config version=\"1\" autoMigrate=file?>")]
        [InlineData("""<?config version="1" autoMigrate="persistent" extra?><SystemMonitor />""", "Invalid processing instruction")]
        [InlineData("""<?config version="1" allowMigration="persistent"?><SystemMonitor />""", "Unknown <?config?> setting 'allowMigration'; expected version, autoMigrate, writeBackupXML.")]
        [InlineData("""<?config version="1" writeBackup="b.xml"?><SystemMonitor />""", "Unknown <?config?> setting 'writeBackup'; expected version, autoMigrate, writeBackupXML.")]
        [InlineData("""<?config version="1" autoMigrate="persistent" autoMigrate="never"?><SystemMonitor />""", "Duplicate <?config?> setting 'autoMigrate'")]
        [InlineData("""<?config version="1" version="1"?><SystemMonitor />""", "Duplicate <?config?> setting 'version'")]
        internal void Header_Invalid_Throws(string xml, string message)
        {
            var ex = Assert.Throws<ConfigurationValueException>(() => CreateMigrator(latest: 1).Read(xml));

            Assert.Contains(message, ex.Message);
        }

        [Fact]
        internal void Header_IsNotConfigurationData()
        {
            // neither the content nor the system directives see it
            var file = CreateMigrator(latest: 1).Read("""<?config version="1" autoMigrate="never"?><?system a="1"?><SystemMonitor timeout="00:01:00" />""");

            Assert.Equal(["a"], file.SystemDirectives.Select(directive => directive.Key));
            Assert.Equal(["timeout"], file.ToConfigNode().Children.Select(child => child.Name));
        }

        #endregion

        #region autoMigrate

        [Theory]
        [InlineData("never", MigrationOption.None)]
        [InlineData("none", MigrationOption.None)]
        [InlineData(" Never ", MigrationOption.None)]
        [InlineData("transient", MigrationOption.Transient)]
        [InlineData("persistent", MigrationOption.Persistent)]
        [InlineData("PERSISTENT", MigrationOption.Persistent)]
        [InlineData("transient|persistent", MigrationOption.Transient | MigrationOption.Persistent)]
        [InlineData(" persistent | Transient ", MigrationOption.Transient | MigrationOption.Persistent)]
        internal void AllowMigration_Parses(string value, MigrationOption expected)
        {
            Assert.Equal(expected, MigrationSettings.ParseOption(value));
        }

        [Fact]
        internal void AllowMigration_Transient_IsNotWritten()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");

            var file = CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path));

            Assert.Equal("2", Version(file.Root.Document!));
            Assert.Contains("version=\"1\"", File.ReadAllText(_path));
        }

        [Theory]
        [InlineData("never|persistent", "cannot be combined")]
        [InlineData("none|transient", "cannot be combined")]
        [InlineData("sometimes", "Invalid autoMigrate")]
        [InlineData("", "must not be empty")]
        [InlineData("  ", "must not be empty")]
        [InlineData("|", "must not be empty")]
        [InlineData(" | ", "must not be empty")]
        internal void AllowMigration_Invalid_Throws(string value, string message)
        {
            var ex = Assert.Throws<ConfigurationValueException>(() =>
                CreateMigrator(latest: 1).Read($"""<?config version="1" autoMigrate="{value}" ?><SystemMonitor />"""));

            Assert.Contains(message, ex.Message);
        }

        [Fact]
        internal void AllowMigration_Never_LeavesAnOutdatedFileUntouched_TheCheckRefusesIt()
        {
            var a = new ModuleA { Min = 2 };

            var migrator = CreateMigrator(latest: 2, a);

            // the migrator stays away completely - no step, no stamp, no write ...
            var file = migrator.Read("""<?config version="1" autoMigrate="never" ?><SystemMonitor />""");

            Assert.Empty(a.Calls);
            Assert.Equal(1u, file.Version);
            Assert.Empty(_log.At(LogLevel.Warning));

            // ... and the version check is the one that terminates on the mismatch
            Assert.Throws<ConfigurationMigrationException>(() => migrator.Registry.Validate(file.Version));
        }

        [Fact]
        internal void AllowMigration_Undeclared_MigratesInMemory_LikeNoHeaderAtAll()
        {
            var a = new ModuleA { Min = 2 };

            var file = CreateMigrator(latest: 2, a).Read("""<?config version="1"?><SystemMonitor />""");

            Assert.Equal([2u], a.Calls.Select(call => call.Version));
            Assert.Equal(2u, file.Version);
        }

        [Fact]
        internal void AllowMigration_Never_AcceptsACurrentFile()
        {
            var file = CreateMigrator(latest: 2).Read("""<?config version="2" autoMigrate="never" ?><SystemMonitor />""");

            Assert.Equal("SystemMonitor", file.RootName);
            Assert.Empty(_log.Entries);
        }

        [Fact]
        internal void WriteBackupXML_WithoutFileMode_IsNotedAtDebug()
        {
            CreateMigrator(latest: 1).Read("""<?config version="1" writeBackupXML="backup.xml" ?><SystemMonitor />""");

            Assert.Contains("writeBackupXML has no effect without autoMigrate=\"persistent\".", _log.At(LogLevel.Debug));
            Assert.Empty(_log.At(LogLevel.Warning));
        }

        [Fact]
        internal void WriteBackupXML_PointingAtTheConfigFile_Throws()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" writeBackupXML="monitor.xml" ?><SystemMonitor />""");

            var ex = Assert.Throws<ConfigurationValueException>(() => CreateMigrator(latest: 1).Read(File.ReadAllText(_path)));

            Assert.StartsWith("writeBackupXML must not point at the configuration file itself", ex.Message);
        }

        #endregion

        #region File mode

        const string FIXTURE_V1 =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<!-- the header comment -->\r\n" +
            "<?config version=\"1\" autoMigrate=\"transient|persistent\" writeBackupXML=\"monitor_v?.xml\" ?>\r\n" +
            "<?system useDBus=\"false\" ?>\r\n" +
            "<SystemMonitor>\r\n" +
            "\r\n" +
            "  <!-- Ethernet &amp; friends -->\r\n" +
            "  <NetworkMonitor name=\"Ethernet\" watchUDPPort=\"9\">\r\n" +
            "    <RemoteHost name=\"nas &amp; co\" />\r\n" +
            "    <Empty></Empty>\r\n" +
            "    <Bare/>\r\n" +
            "  </NetworkMonitor>\r\n" +
            "\r\n" +
            "</SystemMonitor>\r\n";

        [Fact]
        internal void FileMode_WritesTheFile_TheBackup_AndTheHeader_PreservingTheFormatting()
        {
            WriteConfig(FIXTURE_V1);

            var a = new ModuleA { Min = 2, Migrate = RenameWatchPort };

            var migrator = CreateMigrator(latest: 2, a);

            var file = migrator.Read(File.ReadAllText(_path));

            // in memory: migrated
            Assert.Equal("2", Version(file.Root.Document!));

            // the backup holds the original bytes, "?" replaced by the source version
            string backupPath = Path.Combine(_directory, "monitor_v1.xml");
            Assert.Equal(FIXTURE_V1, File.ReadAllText(backupPath));

            // the file: new version, header comment, everything between the tags as it was
            string written = File.ReadAllText(_path);

            Assert.Contains("<!-- the header comment -->\r\n" +
                "<?config version=\"2\" autoMigrate=\"transient|persistent\" writeBackupXML=\"monitor_v?.xml\" ?>\r\n" + // the header is stamped in place ...
                $"<!-- MIGRATION PROTOCOL ({Today})\r\n" +                                                      // ... the protocol goes right below it ...
                "  Version 1 -> 2:\r\n    - /SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort -> renamed to @watchPort\r\n-->\r\n" +
                "<?system useDBus=\"false\" ?>\r\n<SystemMonitor version=\"2\">", written);                        // ... before the system directives

            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", written); // declaration kept
            Assert.Contains("\r\n\r\n  <!-- Ethernet &amp; friends -->\r\n  <NetworkMonitor name=\"Ethernet\" watchPort=\"9\">\r\n" +
                "    <RemoteHost name=\"nas &amp; co\" />\r\n    <Empty></Empty>\r\n    <Bare />\r\n  </NetworkMonitor>\r\n\r\n</SystemMonitor>", written);
            Assert.DoesNotContain("\n\n\n", written.Replace("\r\n", "\n")); // no extra blank lines

            byte[] bytes = File.ReadAllBytes(_path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF); // no BOM added

            Assert.Contains("Configuration file updated to version 2, backup: " + backupPath + ".", _log.At(LogLevel.Information));

            // the file is now current: reading it again is silent and is recognized as our own write
            Assert.True(migrator.IsOwnWrite(written));

            _log.Entries.Clear();
            var again = migrator.Read(written);
            Assert.Empty(_log.Entries);
            Assert.Equal("2", Version(again.Root.Document!));
        }

        [Fact]
        internal void FileMode_KeepsLfNewlines_AndBom_AndWritesNoDeclarationWhenThereWasNone()
        {
            File.WriteAllBytes(_path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("<?config version=\"1\" autoMigrate=\"persistent\" ?><SystemMonitor>\n  <A/>\n</SystemMonitor>\n")]);

            CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path));

            byte[] bytes = File.ReadAllBytes(_path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

            string written = File.ReadAllText(_path);
            Assert.Equal($"<?config version=\"2\" autoMigrate=\"persistent\" ?>\n<!-- MIGRATION PROTOCOL ({Today})\n  Version 1 -> 2.\n-->\n" +
                "<SystemMonitor version=\"2\">\n  <A />\n</SystemMonitor>\n", written);
        }

        [Fact]
        internal void FileMode_MultipleSteps_WriteAfterEveryStep_WithABackupEach()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" writeBackupXML="backup/v?.xml" ?><SystemMonitor />""");
            Directory.CreateDirectory(Path.Combine(_directory, "backup"));

            var a = new ModuleA { Min = 3, Migrate = (d, v) => d.Root!.AnnotateMigration(LogLevel.Information, $"step {v}") };

            CreateMigrator(latest: 3, a).Read(File.ReadAllText(_path));

            string v1 = File.ReadAllText(Path.Combine(_directory, "backup", "v1.xml"));
            string v2 = File.ReadAllText(Path.Combine(_directory, "backup", "v2.xml"));
            string v3 = File.ReadAllText(_path);

            Assert.Contains("version=\"1\"", v1);
            Assert.DoesNotContain("MIGRATION PROTOCOL", v1);

            Assert.Contains("version=\"2\"", v2);
            Assert.Contains("  Version 1 -> 2:\n    - /SystemMonitor -> step 2\n-->", v2);
            Assert.DoesNotContain("Version 2 -> 3", v2);

            // one protocol per run, extended step by step, the steps in order
            Assert.Contains("version=\"3\"", v3);
            Assert.Contains($"<!-- MIGRATION PROTOCOL ({Today})\n  Version 1 -> 2:\n    - /SystemMonitor -> step 2\n  Version 2 -> 3:\n    - /SystemMonitor -> step 3\n-->\n<SystemMonitor", v3);
            Assert.Single(v3.Split("MIGRATION PROTOCOL"), part => part.Contains("Version"));
        }

        [Fact]
        internal void FileMode_ALaterRun_AddsItsOwnProtocol_AfterThePrevious()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><?system a="1"?><SystemMonitor />""");

            // this build knows format 2 ...
            CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path));

            // ... a later one knows format 3: a second protocol, after the first, both in front of the root
            CreateMigrator(latest: 3, new ModuleA { Min = 3, Migrate = (d, _) => d.Root!.AnnotateMigration(LogLevel.Information, "later") })
                .Read(File.ReadAllText(_path));

            // both below the header, in order, before the system directive
            Assert.Contains($"<?config version=\"3\" autoMigrate=\"persistent\" ?>\n" +
                $"<!-- MIGRATION PROTOCOL ({Today})\n  Version 1 -> 2.\n-->\n" +
                $"<!-- MIGRATION PROTOCOL ({Today})\n  Version 2 -> 3:\n    - /SystemMonitor -> later\n-->\n<?system a=\"1\"?><SystemMonitor version=\"3\" />", File.ReadAllText(_path));
        }

        [Fact]
        internal void FileMode_MissingBackupDirectory_IsAWriteFailure()
        {
            // the backup goes where the user said - a mistyped directory is not silently created
            WriteConfig("""<?config version="1" autoMigrate="persistent" writeBackupXML="typo/v?.xml" ?><SystemMonitor />""");

            var ex = Assert.Throws<ConfigurationMigrationException>(() => CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(File.ReadAllText(_path)));

            Assert.StartsWith("Failed to write the migrated configuration file (autoMigrate=\"persistent\"):", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(_directory, "typo")));
            Assert.Contains("version=\"1\"", File.ReadAllText(_path)); // untouched: the backup comes first
        }

        [Fact]
        internal void FileMode_HeaderText_IsSanitized()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");

            var a = new ModuleA { Min = 2, Migrate = (d, _) => d.Root!.AnnotateMigration(LogLevel.Information, "a -- b ---") };

            CreateMigrator(latest: 2, a).Read(File.ReadAllText(_path));

            string written = File.ReadAllText(_path);

            Assert.Contains("-> a - - b - - -", written);
            Assert.DoesNotContain("--'", written);
        }

        [Fact]
        internal void FileMode_WarningStep_StopsWriting_ContinuesInMemory_WithTransient()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""");

            var a = new ModuleA { Min = 3, Migrate = (d, v) => { if (v == 2) d.Root!.AnnotateMigration(LogLevel.Warning, "check this"); } };

            var file = CreateMigrator(latest: 3, a).Read(File.ReadAllText(_path));

            Assert.Equal("3", Version(file.Root.Document!));            // in memory: all the way
            Assert.Contains("version=\"1\"", File.ReadAllText(_path));   // the file: untouched
            Assert.DoesNotContain("MIGRATION PROTOCOL", File.ReadAllText(_path));

            Assert.Contains("The migration produced warnings; the configuration file stays at version 1 from here on, the migration continues in memory only.",
                _log.At(LogLevel.Warning));
        }

        [Fact]
        internal void FileMode_WarningStep_Throws_WithPersistentAlone()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");

            var a = new ModuleA { Min = 3, Migrate = (d, v) => { if (v == 2) d.Root!.AnnotateMigration(LogLevel.Warning, "check this"); } };

            var ex = Assert.Throws<ConfigurationMigrationException>(() => CreateMigrator(latest: 3, a).Read(File.ReadAllText(_path)));

            Assert.StartsWith("The migration from version 1 to version 2 produced warnings, so the configuration file was not updated", ex.Message);
            Assert.Contains("/SystemMonitor -> check this", _log.At(LogLevel.Warning)); // logged before throwing
        }

        [Fact]
        internal void FileMode_WarningInALaterStep_KeepsTheFileAtThePreviousStep()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""");

            var a = new ModuleA { Min = 3, Migrate = (d, v) => { if (v == 3) d.Root!.AnnotateMigration(LogLevel.Warning, "check this"); } };

            var file = CreateMigrator(latest: 3, a).Read(File.ReadAllText(_path));

            Assert.Equal("3", Version(file.Root.Document!));
            Assert.Contains("version=\"2\"", File.ReadAllText(_path)); // written after step 2, not after step 3
            Assert.Contains("stays at version 2 from here on", string.Join("\n", _log.At(LogLevel.Warning)));
        }

        [Fact]
        internal void FileMode_WriteFailure_Warns_AndContinues_WithTransient()
        {
            // a directory where the file should be: no file to update
            string text = """<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""";
            Directory.CreateDirectory(_path);

            var file = CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(text);

            Assert.Equal("2", Version(file.Root.Document!));
            Assert.Contains("Failed to write the migrated configuration file; continuing in memory only.", _log.At(LogLevel.Warning));
        }

        [Fact]
        internal void FileMode_WriteFailure_Throws_WithPersistentAlone()
        {
            string text = """<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""";
            Directory.CreateDirectory(_path);

            var ex = Assert.Throws<ConfigurationMigrationException>(() => CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read(text));

            Assert.StartsWith("Failed to write the migrated configuration file (autoMigrate=\"persistent\"):", ex.Message);
        }

        [Fact]
        internal void FileMode_WriteFailure_DisablesWriting_ForTheRemainingSteps()
        {
            string text = """<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""";
            Directory.CreateDirectory(_path); // a directory where the file should be: every write would fail

            var file = CreateMigrator(latest: 3, new ModuleA { Min = 3 }).Read(text);

            Assert.Equal("3", Version(file.Root.Document!)); // in memory: all the way
            Assert.Single(_log.At(LogLevel.Warning), "Failed to write the migrated configuration file; continuing in memory only."); // only step 2 tried
            Assert.Equal(2, _log.At(LogLevel.Warning).Count(m => m.StartsWith("Migrating the configuration from version"))); // both steps ran
        }

        [Fact]
        internal void FileMode_LockedFile_Warns_AndContinues_WithTransient()
        {
            // FileShare.None is advisory on Unix (a rename over the file succeeds regardless):
            // only Windows refuses both the replace and the in-place rewrite
            if (!OperatingSystem.IsWindows())
                return;

            WriteConfig("""<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""");

            // an exclusive handle: neither the replace nor the in-place fallback can get in
            using var handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None);

            var file = CreateMigrator(latest: 2, new ModuleA { Min = 2 }).Read("""<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""");

            Assert.Equal("2", Version(file.Root.Document!));
            Assert.Contains("Failed to write the migrated configuration file; continuing in memory only.", _log.At(LogLevel.Warning));
            Assert.False(File.Exists(_path + ".tmp"));
        }

        [Fact]
        internal void FileMode_FileHeldOpenForReading_IsRewrittenInPlace()
        {
            // Windows: a reader that shares read/write (the stock file provider mid-load, an
            // editor) refuses the rename over the file - the writer falls back to rewriting it
            // in place, and the reader sees the new content through its own handle
            if (!OperatingSystem.IsWindows())
                return;

            WriteConfig("""<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""");

            using var handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2 });

            migrator.Read("""<?config version="1" autoMigrate="transient|persistent" ?><SystemMonitor />""");

            using var reader = new StreamReader(handle);
            string written = reader.ReadToEnd();

            Assert.Contains("version=\"2\"", written);
            Assert.True(migrator.IsOwnWrite(written));
            Assert.Contains("Configuration file updated to version 2.", _log.At(LogLevel.Information));
            Assert.Contains(_log.At(LogLevel.Debug), m => m.Contains("rewriting it in place"));
            Assert.False(File.Exists(_path + ".tmp"));
        }

        #endregion

        #region Cache

        [Fact]
        internal void Cache_SameText_MigratesOnce_DifferentText_Again()
        {
            var a = new ModuleA { Min = 2 };

            var migrator = CreateMigrator(latest: 2, a);

            var first = migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");
            var second = migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor />""");

            Assert.Same(first, second);
            Assert.Single(a.Calls);
            Assert.Single(_log.At(LogLevel.Warning));

            var third = migrator.Read("""<?config version="1" autoMigrate="transient"?><SystemMonitor timeout="1min" />""");

            Assert.NotSame(first, third);
            Assert.Equal(2, a.Calls.Count);
            Assert.Equal(2, _log.At(LogLevel.Warning).Count());
        }

        [Fact]
        internal void OwnWrite_IsRetired_OnceAForeignTextHasBeenRead()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");

            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2 });

            migrator.Read(File.ReadAllText(_path));

            string written = File.ReadAllText(_path);
            Assert.True(migrator.IsOwnWrite(written));

            // a real edit afterwards ...
            migrator.Read(written.Replace("<SystemMonitor version=\"2\" />", "<SystemMonitor version=\"2\" timeout=\"00:10:00\" />"));

            // ... makes a later revert to the written text a change like any other
            Assert.False(migrator.IsOwnWrite(written));
        }

        [Fact]
        internal void OwnWrite_IntermediateStepText_IsNotMigratedAgain_AndKeepsItsBackup()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient|persistent" writeBackupXML="b_v?.xml" ?><SystemMonitor />""");

            // step 3 reads the file as it is on disk right then: the text step 2 wrote (a slow
            // reader - a provider reload, the pipeline - can see exactly that text)
            string? intermediate = null;
            var a = new ModuleA { Min = 3, Migrate = (_, v) => { if (v == 3) intermediate = File.ReadAllText(_path); } };

            var migrator = CreateMigrator(latest: 3, a);

            var file = migrator.Read(File.ReadAllText(_path));

            Assert.NotNull(intermediate);
            Assert.Contains("version=\"2\"", intermediate);
            Assert.True(migrator.IsOwnWrite(intermediate));

            _log.Entries.Clear();

            var again = migrator.Read(intermediate); // e.g. the content provider's reload

            Assert.Same(file, again); // the run's result: the same data, already at version 3
            Assert.Empty(_log.Entries);
            Assert.Contains("version=\"2\"", File.ReadAllText(Path.Combine(_directory, "b_v2.xml"))); // not overwritten with v3 content
            Assert.Contains("version=\"3\"", File.ReadAllText(_path));
        }

        [Fact]
        internal void Migrate_Bytes_DecodesWithBomDetection()
        {
            var migrator = CreateMigrator(latest: 1);

            byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""<?config version="1" autoMigrate="transient"?><SystemMonitor marker="ü" />""")];

            Assert.Same(bytes, migrator.Migrate(bytes)); // current: served as-is

            var file = migrator.Read(ConfigurationText.Decode(bytes));

            Assert.Equal("ü", file.Root.Attribute("marker")!.Value);
        }

        [Fact]
        internal void Migrate_Bytes_HonoursTheDeclaredEncoding()
        {
            var migrator = CreateMigrator(latest: 1);

            byte[] bytes = Encoding.Latin1.GetBytes("""<?xml version="1.0" encoding="iso-8859-1"?><?config version="1" autoMigrate="transient"?><SystemMonitor marker="Büro" />""");
            Assert.Contains((byte)0xFC, bytes); // a single byte, no BOM

            var file = migrator.Read(ConfigurationText.Decode(bytes));

            Assert.Equal("Büro", file.Root.Attribute("marker")!.Value);
        }

        [Fact]
        internal void FileMode_KeepsTheCharactersOfALatin1File()
        {
            File.WriteAllBytes(_path, Encoding.Latin1.GetBytes(
                """<?xml version="1.0" encoding="iso-8859-1"?><?config version="1" autoMigrate="persistent" ?><SystemMonitor marker="Büro" />"""));

            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2 });

            var file = migrator.Read(ConfigurationText.ReadFile(_path));

            Assert.Equal("Büro", file.Root.Attribute("marker")!.Value);

            byte[] written = File.ReadAllBytes(_path);

            Assert.Contains((byte)0xFC, written);
            Assert.DoesNotContain("&#xFFFD;", Encoding.Latin1.GetString(written));
            Assert.Contains("marker=\"Büro\"", Encoding.Latin1.GetString(written));
            Assert.True(migrator.IsOwnWrite(ConfigurationText.ReadFile(_path)));
        }

        #endregion

        #region Integration with the source

        [Fact]
        internal void Source_EndToEnd_ProvidersServeTheMigratedShape()
        {
            WriteConfig("""
                <?config version="1" autoMigrate="transient" ?>
                <?system useDBus="false" ?>
                <SystemMonitor timeout="00:10:00">
                  <NetworkMonitor name="Ethernet" watchUDPPort="9" />
                </SystemMonitor>
                """);

            var source = new ExtendedXmlConfigurationSource(_path);

            // the isolated composition: the migration layer decorates the source's file
            // provider - the source itself knows nothing about it
            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2, Migrate = RenameWatchPort });

            source.FileProvider = new MigratingFileProvider(source.FileProvider!, migrator, source.Path!);

            var content = new ConfigurationBuilder().Add(source).Build();
            var root = new ConfigurationBuilder().Add(((IRootConfigurationSource)source).RootSource).Build();

            Assert.Equal("9", content["NetworkMonitor:watchPort"]);
            Assert.Null(content["NetworkMonitor:watchUDPPort"]);
            Assert.Null(content["version"]); // the format declaration is not data

            Assert.Equal("false", root["useDBus"]);

            // one file text, one migration, one set of log lines
            Assert.Single(_log.At(LogLevel.Warning));
            Assert.Single(_log.At(LogLevel.Information));

            // passthrough: the root attributes are the data the strict binder sees
            var config = StrictConfigurationBinder.Get<SystemMonitorConfig>(content, o => o.BindNonPublicProperties = true);

            Assert.NotNull(config);
            Assert.Equal(TimeSpan.FromMinutes(10), config.Timeout);
        }

        [Fact]
        internal void Source_FileMode_ThroughTheProvider_UpdatesTheFile()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" writeBackupXML="old.xml" ?><SystemMonitor />""");

            var source = new ExtendedXmlConfigurationSource(_path);

            var migrator = CreateMigrator(latest: 2, new ModuleA { Min = 2 });

            source.FileProvider = new MigratingFileProvider(source.FileProvider!, migrator, source.Path!);

            var content = new ConfigurationBuilder().Add(source).Build();

            Assert.Null(content["version"]);
            Assert.Contains("<?config version=\"2\" autoMigrate=\"persistent\" writeBackupXML=\"old.xml\" ?>", File.ReadAllText(_path));
            Assert.Contains("<?config version=\"1\" autoMigrate=\"persistent\" writeBackupXML=\"old.xml\" ?>", File.ReadAllText(Path.Combine(_directory, "old.xml")));
            Assert.DoesNotContain(_log.Entries, entry => entry.Exception is not null);
            Assert.False(File.Exists(_path + ".tmp"));

            // the decorator read and closed the file before the migration wrote it, so the
            // replace was atomic - no reader ever holds the file open across a migration
            Assert.DoesNotContain(_log.At(LogLevel.Debug), m => m.Contains("rewriting it in place"));
        }

        [Fact]
        internal void SystemMonitorConfig_BindsWithTheMigrationAttributesPresent()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient" writeBackupXML="b.xml" ?><SystemMonitor timeout="00:05:00" />""");

            var content = new ConfigurationBuilder().Add(new ExtendedXmlConfigurationSource(_path)).Build();

            var config = StrictConfigurationBinder.Get<SystemMonitorConfig>(content, o => o.BindNonPublicProperties = true);

            Assert.NotNull(config);
            Assert.Equal(TimeSpan.FromMinutes(5), config.Timeout);
        }

        [Fact]
        internal void Cache_ThePreMigrationText_IsServedToAStaleReader_ButMigratedAgainWhenTheFileHoldsItAgain()
        {
            WriteConfig(FIXTURE_V1);

            var a = new ModuleA { Min = 2, Migrate = RenameWatchPort };

            var migrator = CreateMigrator(latest: 2, a);

            migrator.Read(FIXTURE_V1); // migrates and writes the file

            string written = File.ReadAllText(_path);
            Assert.Contains("version=\"2\"", written);
            Assert.Single(a.Calls);

            // a reader that opened the file before the write hands in the old text while the file
            // holds the written one: served from the cache, nothing runs again
            Assert.Same(migrator.Read(FIXTURE_V1), migrator.Read(written));
            Assert.Single(a.Calls);

            // the file reverted to the very same text (a restored backup): migrated and written again
            WriteConfig(FIXTURE_V1);

            var again = migrator.Read(FIXTURE_V1);

            Assert.Equal(2, a.Calls.Count);
            Assert.Equal("2", Version(again.Root.Document!));
            Assert.Equal(written, File.ReadAllText(_path));
            Assert.Equal(2, _log.Messages.Count(message => message.StartsWith("Migrating the configuration from version 1")));
            Assert.True(migrator.IsOwnWrite(written));
        }

        #endregion

        #region Capability

        // a module that raised its MinVersion but cannot migrate (no IXConfigurationMigration)
        private sealed class PlainModule : ConfigurableModule
        {
            public uint Min { get; init; } = 1;

            protected internal override uint MinVersion => Min;

            protected internal override uint MaxVersion => uint.MaxValue;
        }

        [Fact]
        internal void Capability_ParticipantWithoutMigrationSupport_AbortsBeforeAnyStep()
        {
            WriteConfig(FIXTURE_V1); // transient|persistent with a backup: nothing of that may happen

            var a = new ModuleA { Min = 2, Migrate = RenameWatchPort };

            var migrator = CreateMigrator(latest: 2, a, new PlainModule { Min = 2 });

            var ex = Assert.Throws<ConfigurationMigrationException>(() => migrator.Read(File.ReadAllText(_path)));

            Assert.Contains($"<{typeof(PlainModule).FullName}> requires configuration format version 2, but cannot migrate this configuration automatically", ex.Message);
            Assert.Contains("from version 1 to version 2 manually", ex.Message);

            Assert.Empty(a.Calls);                                          // checked before the first step
            Assert.Equal(FIXTURE_V1, File.ReadAllText(_path));              // nothing written
            Assert.False(File.Exists(Path.Combine(_directory, "monitor_v1.xml")));
            Assert.DoesNotContain(_log.Messages, message => message.StartsWith("Migrating"));
        }

        [Fact]
        internal void Capability_ModuleWithoutSupport_ButNotAParticipant_DoesNotBlock()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient"?><SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" /></SystemMonitor>""");

            var migrator = CreateMigrator(latest: 2, new PlainModule { Min = 1 }, new ModuleA { Min = 2, Migrate = RenameWatchPort });

            var file = migrator.Read(File.ReadAllText(_path));

            Assert.Equal("2", Version(file.Root.Document!));
            Assert.NotNull(file.Root.Element("NetworkMonitor")!.Attribute("watchPort"));
        }

        [Fact]
        internal void Capability_CurrentFile_NeedsNoSupport()
        {
            WriteConfig("""<?config version="2" autoMigrate="transient"?><SystemMonitor />""");

            var migrator = CreateMigrator(latest: 2, new PlainModule { Min = 2 });

            var file = migrator.Read(File.ReadAllText(_path));

            Assert.Equal(2u, file.Version);
            Assert.Empty(_log.Entries);
        }

        #endregion

        #region Change tracking

        [Fact]
        internal void Tracking_ChangesOutsideTheHelpers_AreWarnings_WithThePathBeforeTheChange_AndStopTheFileWrite()
        {
            WriteConfig(FIXTURE_V1);

            var a = new ModuleA
            {
                Min = 2,
                Migrate = (d, _) =>
                {
                    var monitor = d.Root!.Element("NetworkMonitor")!;

                    d.Root.SetAttributeValue("added", "x");                 // Add
                    monitor.Element("RemoteHost")!.Remove();                // Remove
                    monitor.Attribute("watchUDPPort")!.Value = "10";        // Value
                    monitor.Name = "NetMon";                                // Name
                },
            };

            var migrator = CreateMigrator(latest: 2, a);

            var file = migrator.Read(File.ReadAllText(_path));

            Assert.Equal("2", Version(file.Root.Document!)); // migrated in memory (transient|persistent)

            var warnings = _log.At(LogLevel.Warning).ToList();

            Assert.Contains("/SystemMonitor/@added -> added outside a migration helper", warnings);
            Assert.Contains("/SystemMonitor/NetworkMonitor[@name='Ethernet']/RemoteHost[@name='nas & co'] -> removed outside a migration helper", warnings);
            Assert.Contains("/SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort -> changed from \"9\" to \"10\" outside a migration helper", warnings);
            Assert.Contains("/SystemMonitor/NetworkMonitor[@name='Ethernet'] -> renamed to <NetMon> outside a migration helper", warnings);
            Assert.Contains(warnings, message => message.Contains("stays at version 1"));

            Assert.Equal(FIXTURE_V1, File.ReadAllText(_path)); // a warned step is not written
            Assert.Empty(_log.At(LogLevel.Information));
        }

        [Fact]
        internal void Tracking_EndsWithTheRun_AndLeavesNoTrackerBehind()
        {
            WriteConfig("""<?config version="1" autoMigrate="transient"?><SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" /></SystemMonitor>""");

            var file = CreateMigrator(latest: 2, new ModuleA { Min = 2, Migrate = RenameWatchPort }).Read(File.ReadAllText(_path));

            var document = file.Root.Document!;

            Assert.Null(XMigrationTracker.Of(document));

            document.Root!.SetAttributeValue("later", "x"); // a change after the run: nobody listens, nothing is recorded

            Assert.Empty(document.TakeMigrationAnnotations());
        }

        #endregion
    }
}
