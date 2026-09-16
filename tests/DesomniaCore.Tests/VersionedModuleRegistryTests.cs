using Autofac;
using MadWizard.Desomnia.Application.Registry;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class VersionedModuleRegistryTests
    {
        private sealed class FirstConfig;
        private sealed class SecondConfig;

        private sealed class FirstModule : ConfigurableModule<FirstConfig>
        {
            protected override void Load(ContainerBuilder builder, FirstConfig config) { }
        }

        private abstract class IntermediateModule<T> : ConfigurableModule<T>
        {
            protected override void Load(ContainerBuilder builder, T config) { }
        }

        private sealed class IndirectModule : IntermediateModule<SecondConfig>;

        private sealed class PlainConfigurableModule : ConfigurableModule;

        [Fact]
        public void ConfigTypes_AreDiscoveredFromGenericBaseTypes_InRegistrationOrder()
        {
            var registry = new VersionedModuleRegistry();

            registry.Register(new FirstModule());
            registry.Register(new PlainConfigurableModule());
            registry.Register(new IndirectModule());
            registry.Lock();

            Assert.Equal([typeof(FirstConfig), typeof(SecondConfig)], registry.ConfigTypes);
        }
    }
}
