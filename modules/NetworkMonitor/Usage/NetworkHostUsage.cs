using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Usage;

namespace MadWizard.Desomnia.Network
{
    public class NetworkHostUsage(NetworkHost host, long bytes) : NetworkUsage(bytes)
    {
        public string Name => host.Name;

        public NetworkHost Host => host;

        private IEnumerable<NetworkServiceUsage> ServiceTokens => Tokens.OfType<NetworkServiceUsage>();
        private IEnumerable<VirtualMachineUsage> VirtualMachineTokens => Tokens.OfType<VirtualMachineUsage>();

        public override string ToString()
        {
            string str = Name;

            if (VirtualMachineTokens.Any())
            {
                str += "::VM";
            }

            if (ServiceTokens.Any())
            {
                str += ":" + string.Join(':', ServiceTokens);
            }

            return str;
        }
    }
}
