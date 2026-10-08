using MadWizard.Desomnia.Network.Manager;

namespace MadWizard.Desomnia.Network.Interface.Configuration
{
    internal class WakeOnLANConfigurator(WakeOnLANMode target, IWakeOnLANManager? manager = null) : Configurator<WakeOnLANMode>(target)
    {
        protected internal override WakeOnLANMode ReadConfiguration() =>
            manager?.Modes ?? throw new NotSupportedException("IWakeOnLANManager not available");

        protected internal override async Task ApplyConfiguration(WakeOnLANMode value)
        {
            if (manager is null || (value & ~manager.SupportedModes) != 0)
                throw new NotSupportedException($"Configuration {value} is not supported.");

            manager.Modes = value;
        }
    }
}
