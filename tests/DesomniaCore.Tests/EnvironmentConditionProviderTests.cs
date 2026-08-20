using Autofac;
using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Environments.Conditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// Namespaced condition attributes: <c>env:USER="Kevin"</c> with
    /// <c>xmlns:env="environment:process"</c> resolves the
    /// <see cref="IEnvironmentConditionProvider"/> registered for the namespace URI, which
    /// receives the local name and the value.
    /// </summary>
    public class EnvironmentConditionProviderTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;
        private readonly string _variable = $"DESOMNIA_TEST_{Guid.NewGuid():N}";

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_variable, null);

            Directory.Delete(_directory, recursive: true);
        }

        private string WriteConfig(string content)
        {
            var path = Path.Combine(_directory, "monitor.xml");

            File.WriteAllText(path, content);

            return path;
        }

        #region Parser

        private static EnvironmentParser.Result Parse(string xml) => EnvironmentParser.Parse(XDocument.Parse(xml));

        [Fact]
        internal void Parser_TreatsNamespacedAttributes_AsConditions_AndSkipsTheDeclaration()
        {
            var result = Parse("""
                <EnvironmentMonitor xmlns:env="environment:process">
                  <Environment env:USER="Kevin" power="ac"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var block = Assert.Single(result.Blocks);

            Assert.Collection(block.ConditionAttributes,
                condition =>
                {
                    Assert.Equal("environment:process", condition.Namespace);
                    Assert.Equal("USER", condition.Name);
                    Assert.Equal("Kevin", condition.Value);
                    Assert.Equal("env:USER", condition.DisplayName);
                },
                condition =>
                {
                    Assert.Null(condition.Namespace);
                    Assert.Equal("power", condition.Name);
                });

            Assert.Equal("env:USER=\"Kevin\" power=\"ac\"", block.DisplayName);
        }

        [Fact]
        internal void Parser_AllowsTheDeclaration_OnTheEnvironmentElementItself()
        {
            var result = Parse("""
                <EnvironmentMonitor>
                  <Environment xmlns:env="environment:process" env:USER="Kevin"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var condition = Assert.Single(Assert.Single(result.Blocks).ConditionAttributes);

            Assert.Equal("environment:process", condition.Namespace);
        }

        [Fact]
        internal void Parser_NamespacedStructuralNames_AreConditionsNotStructure()
        {
            // env:name must not collide with the structural name attribute
            var result = Parse("""
                <EnvironmentMonitor xmlns:env="environment:process">
                  <Environment name="real" env:name="value"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var block = Assert.Single(result.Blocks);

            Assert.Equal("real", block.Name);

            var condition = Assert.Single(block.ConditionAttributes);
            Assert.Equal("name", condition.Name);
            Assert.Equal("environment:process", condition.Namespace);
        }

        [Fact]
        internal void Parser_RejectsNamespacedConditions_OnTheDefaultEnvironment()
        {
            var ex = Assert.Throws<ConfigurationValueException>(() => Parse("""
                <EnvironmentMonitor xmlns:env="environment:process">
                  <Environment test="on"><SystemMonitor /></Environment>
                  <DefaultEnvironment env:USER="Kevin"><SystemMonitor /></DefaultEnvironment>
                </EnvironmentMonitor>
                """));

            Assert.Contains("env:USER", ex.Message);
        }

        #endregion

        #region EnvironmentVariableCondition

        [Fact]
        public void EnvironmentVariable_MatchesExactly_CaseSensitive()
        {
            Environment.SetEnvironmentVariable(_variable, "Kevin");

            Assert.True(new EnvironmentVariableCondition(_variable, "Kevin").IsSatisfied());
            Assert.False(new EnvironmentVariableCondition(_variable, "kevin").IsSatisfied());
            Assert.False(new EnvironmentVariableCondition(_variable, "Kev").IsSatisfied());
        }

        [Fact]
        public void EmptyValue_MatchesUnsetOrEmptyVariable()
        {
            Assert.True(new EnvironmentVariableCondition(_variable, "").IsSatisfied()); // unset

            Environment.SetEnvironmentVariable(_variable, "set");
            Assert.False(new EnvironmentVariableCondition(_variable, "").IsSatisfied());
        }

        #endregion

        #region End-to-end through the pipeline

        private static ILifetimeScope ConditionScope()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<ProcessEnvironmentConditionProvider>()
                .Named<IEnvironmentConditionProvider>(ProcessEnvironmentConditionProvider.Namespace);

            return builder.Build();
        }

        private static ConfigurationPipeline CreatePipeline(string path)
        {
            var source = new ExtendedXmlConfigurationSource(path);

            var monitor = new EnvironmentMonitor { Logger = NullLogger.Instance };

            return new ConfigurationPipeline(source, monitor, ConditionScope(), new ModuleRegistry { Logger = NullLogger.Instance })
            {
                Logger = NullLogger.Instance,
            };
        }

        [Fact]
        internal void NamespacedCondition_DecidesTheMergedConfiguration()
        {
            Environment.SetEnvironmentVariable(_variable, "Kevin");

            var path = WriteConfig($"""
                <EnvironmentMonitor xmlns:env="environment:process">
                  <Environment env:{_variable}="Kevin"><SystemMonitor marker="matched" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var pipeline = CreatePipeline(path);

            pipeline.Start();

            var configuration = new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();

            Assert.Equal("matched", configuration["marker"]);
        }

        [Fact]
        internal void UnregisteredNamespace_IsAConfigurationError()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor xmlns:custom="something:unknown">
                  <Environment custom:key="value"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var pipeline = CreatePipeline(path);

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.Start);

            Assert.Contains("something:unknown", ex.Message);
            Assert.Contains("custom:key", ex.Message);
        }

        #endregion
    }
}
