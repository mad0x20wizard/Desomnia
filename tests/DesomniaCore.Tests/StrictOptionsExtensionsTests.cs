using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The strict options wiring (<see cref="StrictOptionsExtensions"/>): the standard options
    /// interfaces, bound the modules' way - value variations, non-public setters, invalid values
    /// throw - and following the configuration's reload token like the stock Configure(IConfiguration).
    /// </summary>
    public class StrictOptionsExtensionsTests
    {
        public sealed class TestOptions
        {
            public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

            public string? Marker { get; private set; }

            public int Count { get; set; }
        }

        private static IConfigurationRoot Configuration(params (string Key, string? Value)[] pairs)
            => new ConfigurationBuilder()
                .AddInMemoryCollection(pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
                .Build();

        private static ServiceProvider Services(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();

            configure(services);

            return services.BuildServiceProvider();
        }

        [Fact]
        public void ConfigureStrict_AppliesTheValueVariations_AndBindsNonPublicSetters()
        {
            var configuration = Configuration(("Test:interval", "90s"), ("Test:marker", "set"));

            using var services = Services(s => s.ConfigureStrict<TestOptions>(configuration.GetSection("Test")));

            var options = services.GetRequiredService<IOptions<TestOptions>>().Value;

            Assert.Equal(TimeSpan.FromSeconds(90), options.Interval);  // "90s" - the stock binder would reject it
            Assert.Equal("set", options.Marker);                       // private setter - the stock default skips it
        }

        [Fact]
        public void BindStrict_IsTheOptionsBuilderShape_OfTheSame()
        {
            var configuration = Configuration(("interval", "5min"));

            using var services = Services(s => s.AddOptions<TestOptions>().BindStrict(configuration));

            Assert.Equal(TimeSpan.FromMinutes(5), services.GetRequiredService<IOptions<TestOptions>>().Value.Interval);
        }

        [Fact]
        public void InvalidValue_Throws_WhereTheOptionsAreRead()
        {
            var configuration = Configuration(("count", "many"));

            using var services = Services(s => s.ConfigureStrict<TestOptions>(configuration));

            var options = services.GetRequiredService<IOptions<TestOptions>>();

            // lazily, on first access - and the strict binder's exception type, unwrapped
            Assert.Throws<ConfigurationValueException>(() => options.Value);
        }

        [Fact]
        public void EmptyConfiguration_BindsTheDefaults()
        {
            using var services = Services(s => s.ConfigureStrict<TestOptions>(Configuration()));

            var options = services.GetRequiredService<IOptions<TestOptions>>().Value;

            Assert.Equal(TimeSpan.FromMinutes(1), options.Interval);
            Assert.Null(options.Marker);
        }

        [Fact]
        public void BinderOptions_RunOnTopOfTheStrictDefaults()
        {
            var configuration = Configuration(("marker", "set"));

            using var services = Services(s => s.ConfigureStrict<TestOptions>(configuration,
                binder => binder.BindNonPublicProperties = false)); // opting out of the default

            Assert.Null(services.GetRequiredService<IOptions<TestOptions>>().Value.Marker);
        }

        [Fact]
        public void NamedOptions_BindTheirOwnSections()
        {
            var configuration = Configuration(("A:count", "1"), ("B:count", "2"));

            using var services = Services(s =>
            {
                s.ConfigureStrict<TestOptions>("a", configuration.GetSection("A"), configureBinder: null);
                s.AddOptions<TestOptions>("b").BindStrict(configuration.GetSection("B"));
            });

            var monitor = services.GetRequiredService<IOptionsMonitor<TestOptions>>();

            Assert.Equal(1, monitor.Get("a").Count);
            Assert.Equal(2, monitor.Get("b").Count);
            Assert.Equal(0, monitor.CurrentValue.Count); // the default name is not configured
        }

        [Fact]
        public void OptionsMonitor_FollowsTheConfigurationsReload()
        {
            var configuration = Configuration(("Test:interval", "1s"));

            using var services = Services(s => s.ConfigureStrict<TestOptions>(configuration.GetSection("Test")));

            var monitor = services.GetRequiredService<IOptionsMonitor<TestOptions>>();

            Assert.Equal(TimeSpan.FromSeconds(1), monitor.CurrentValue.Interval);

            TestOptions? changed = null;
            using var subscription = monitor.OnChange(options => changed = options);

            configuration["Test:interval"] = "2s";
            configuration.Reload();

            Assert.Equal(TimeSpan.FromSeconds(2), monitor.CurrentValue.Interval);
            Assert.NotNull(changed);
            Assert.Equal(TimeSpan.FromSeconds(2), changed!.Interval);
        }
    }
}
