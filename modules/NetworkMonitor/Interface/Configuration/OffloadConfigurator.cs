using MadWizard.Desomnia.Network.Manager;

namespace MadWizard.Desomnia.Network.Interface.Configuration
{
    internal class OffloadConfigurator(OffloadProtocol target, IProtocolOffloadManager? manager = null) : Configurator<OffloadProtocol>(target)
    {
        protected internal override OffloadProtocol ReadConfiguration() =>
            manager?.OffloadProtocols ?? throw new NotSupportedException("IProtocolOffloadManager not available");

        protected internal override async Task ApplyConfiguration(OffloadProtocol value)
        {
            if (manager is null || (value & ~manager.SupportedProtocols) != 0)
                throw new NotSupportedException($"Configuration {value} is not supported.");

            manager.OffloadProtocols = value;
        }
    }
}
