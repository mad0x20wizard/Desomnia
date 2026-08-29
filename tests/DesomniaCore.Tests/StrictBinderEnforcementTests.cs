using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The binder-level halves of the name/key semantics: collection items bind from their
    /// index keys with nothing synthesized into an unset Name property, and a C# `required`
    /// member is actually enforced (reflection construction bypasses the compiler's check).
    /// </summary>
    public class StrictBinderEnforcementTests
    {
        private static IConfigurationRoot Config(params (string Key, string? Value)[] pairs)
            => new ConfigurationBuilder().AddInMemoryCollection(
                pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

        private static T? Bind<T>(IConfigurationRoot config)
            => StrictConfigurationBinder.Get<T>(config, options => options.BindNonPublicProperties = true);

        #region Index-keyed items, Name as pure data

        private sealed class HostList
        {
            public List<HostItem> Host { get; } = [];
        }

        private sealed class HostItem
        {
            public string? Name { get; set; }
            public string? Address { get; set; }
        }

        [Fact]
        public void CollectionItems_BindFromIndexKeys_InOrder()
        {
            var config = Config(
                ("Host:0:address", "10.0.0.1"),
                ("Host:1:name", "beta"),
                ("Host:1:address", "10.0.0.2"));

            var bound = Bind<HostList>(config)!;

            Assert.Equal(2, bound.Host.Count);
            Assert.Equal("10.0.0.1", bound.Host[0].Address);
            Assert.Equal("10.0.0.2", bound.Host[1].Address);
        }

        [Fact]
        public void UnsetName_StaysNull_NothingIsSynthesized()
        {
            var config = Config(
                ("Host:0:address", "10.0.0.1"),
                ("Host:1:name", "beta"));

            var bound = Bind<HostList>(config)!;

            Assert.Null(bound.Host[0].Name);
            Assert.Equal("beta", bound.Host[1].Name);
        }

        #endregion

        #region Required members

        private sealed class RequiredName
        {
            public required string Name { get; set; }
            public string? Comment { get; set; }
        }

        private sealed class RequiredNameWithDefault
        {
            public required string Name { get; set; } = "fallback";
        }

        private sealed class RequiredHostList
        {
            public List<RequiredName> Host { get; } = [];
        }

        [Fact]
        public void RequiredMember_MissingFromTheConfiguration_Throws()
        {
            var config = Config(("Comment", "no name anywhere"));

            var ex = Assert.Throws<ConfigurationValueException>(() => Bind<RequiredName>(config));

            Assert.Contains("Name", ex.Message);
        }

        [Fact]
        public void RequiredMember_SetByTheConfiguration_Binds()
        {
            var bound = Bind<RequiredName>(Config(("Name", "alpha")))!;

            Assert.Equal("alpha", bound.Name);
        }

        [Fact]
        public void RequiredMember_WithAnInitializerDefault_IsSatisfied()
        {
            var bound = Bind<RequiredNameWithDefault>(Config(("Unrelated", "x")))!;

            Assert.Equal("fallback", bound.Name);
        }

        [Fact]
        public void RequiredMember_OfACollectionItem_IsEnforcedPerItem()
        {
            var config = Config(
                ("Host:0:name", "alpha"),
                ("Host:1:comment", "nameless"));

            var ex = Assert.Throws<ConfigurationValueException>(() => Bind<RequiredHostList>(config));

            Assert.Contains("Host:1", ex.Message);
        }

        #endregion
    }
}
