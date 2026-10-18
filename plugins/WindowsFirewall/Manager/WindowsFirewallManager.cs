using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Firewall.WindowsFirewall.Native;

namespace MadWizard.Desomnia.Firewall.WindowsFirewall.Manager
{
    public sealed class WindowsFirewallManager : IFirewallManager
    {
        public IDisposable Install(NetworkHost host, IReadOnlyCollection<FirewallRule> rules)
        {
            if (host is not LocalHost)
                throw new NotSupportedException("The WFP prototype supports the physical local host only.");

            return WfpSession.Install(host.Name, rules);
        }
    }
}
