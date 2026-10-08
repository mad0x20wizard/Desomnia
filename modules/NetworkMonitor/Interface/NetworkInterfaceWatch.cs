using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Manager;

namespace MadWizard.Desomnia.Network.Interface
{
    /// <summary>One present interface, with settings resolved in configuration order.</summary>
    internal sealed class NetworkInterfaceWatch(INetworkInterface @interface, bool? disabled, NetworkInterfaceState allowed, bool monitor)
    {
        public INetworkInterface Interface => @interface;
        public bool Monitor => monitor;
        private bool _initialized;

        public void Reconcile()
        {
            // Reading actual state lets the manager recognize external changes and update
            // restoration before we decide whether to enforce or tolerate them.
            bool actual = @interface.IsDisabled;
            if (disabled is bool target)
            {
                if (!_initialized || (!allowed.HasFlag(NetworkInterfaceState.Disabled) && actual != target))
                    @interface.ShouldBeDisabled = target;
            }
            else if (!_initialized || @interface.ShouldBeDisabled is not null)
            {
                @interface.ShouldBeDisabled = null;
            }
            _initialized = true; // failures remain eligible for another initial attempt
        }
    }
}
