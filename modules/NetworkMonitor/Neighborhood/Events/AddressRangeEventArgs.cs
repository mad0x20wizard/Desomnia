using NetTools;

namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class AddressRangeEventArgs(IPAddressRange range) : EventArgs
    {
        public IPAddressRange IPAddressRange => range;
    }
}
