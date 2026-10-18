using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Firewall.Manager
{
    public enum FirewallAction { Block, Permit }

    // Ordering is relative to other rules in this policy, not other firewall providers.
    public enum FirewallPriority { Default = 100, Permit = 200, Deny = 300 }

    public sealed record FirewallRule(IPAddress LocalAddress, IPService Service,
        IPAddressRange Source, FirewallAction Action, FirewallPriority Priority);
}
