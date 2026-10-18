using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Firewall.Manager
{
    /// <summary>Prototype contract: the returned lease owns a startup snapshot of inbound rules.</summary>
    public interface IFirewallManager
    {
        IDisposable Install(NetworkHost host, IReadOnlyCollection<FirewallRule> rules);
    }
}
