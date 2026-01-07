using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia
{
    public abstract class ConfigurableModule : Module
    {
        protected internal virtual void ConfigureConfigurationSource(ExtendedXmlConfigurationSource source) { }
    }

    public abstract class ConfigurableModule<T> : ConfigurableModule
    {
        public T Config { get; private set; } = default!;

        protected internal override void Build(HostApplicationBuilder builder)
        {
            base.Build(builder);

            Config = builder.Configuration.Get<T>(opt => opt.BindNonPublicProperties = true)
                ?? throw new Exception($"Configuration binding for <{typeof(T).Name}> failed.");
        }
    }
}
