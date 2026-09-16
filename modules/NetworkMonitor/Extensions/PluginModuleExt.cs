using Autofac.Builder;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Configuration;

namespace MadWizard.Desomnia.Network.Extensions
{
    public static class PluginModuleExt
    {
        extension (MetadataConfiguration<Network.PluginModule.Metadata> meta)
        {
            public MetadataConfiguration<Network.PluginModule.Metadata> ForNetwork(NetworkMonitorConfig config)
            {
                if (config.Ordinal < 0)
                    throw new ConfigurationException("NetworkMonitorConfig has no ordinal");

                return meta.For(m => m.Network, config.Ordinal);
            }
        }
    }
}
