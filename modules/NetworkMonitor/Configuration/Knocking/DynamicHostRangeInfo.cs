using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Knocking
{
    public class DynamicHostRangeInfo : NetworkHostRangeInfo
    {
        public string?              KnockMethod         { get; set; }

        public ushort?              KnockPort           { get; set; }
        public IPProtocol?          KnockProtocol       { get; set; }
        public TimeSpan?            KnockTimeout        { get; set; }

        public bool                 ProofIP             { get; set; } = false;
        public TimeSpan?            ProofTime           { get; set; } = null;

        public IList<SharedSecretData> SharedSecret     { get; set; } = [];

        // Packet Filter Rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; set; } = [];
        // KnockEvent Filter Rules
        public IList<ServiceFilterRuleInfo> ServiceFilterRule { get; set; } = [];
    }

    public class SharedSecretData : KeyData
    {
        public string?  Label { get; init; }

        public KeyData? Key { get; set; }
        public AuthKeyData? AuthKey { get; set; }

        public bool Passthrough { get; set; } = false;
    }

    public class KeyData
    {
        public string? Encoding { get; set; }

        public string? Text { get; set; }
    }

    public class AuthKeyData : KeyData
    {
        public DigestType Type { get; set; } = DigestType.Default;
    }
}
