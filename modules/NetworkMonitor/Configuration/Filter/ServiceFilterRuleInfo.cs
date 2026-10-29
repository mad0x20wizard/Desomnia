using MadWizard.Desomnia.Network.Filter.Rules;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class ServiceFilterRuleInfo : IPFilterRuleInfo, IIEnumerable<IPService>
    {
        public string? Name { get; set; }

        public IPProtocol Protocol { get; set; } = IPProtocol.TCP;

        public ushort Port { get; set; }

        IEnumerator<IPService> IEnumerable<IPService>.GetEnumerator()
        {
            if (!(Protocol.IsSingle || Protocol.IsMultiple))
                throw new FormatException($"Unsupported service filter protocol: {Protocol}.");
            if (Type == FilterRuleType.Must && Protocol.IsMultiple)
                throw new FormatException("A Must service filter requires exactly one protocol. Use May for TCP/UDP alternatives.");

            if (Protocol.HasFlag(IPProtocol.TCP))
                yield return new(IPProtocol.TCP, Port);

            if (Protocol.HasFlag(IPProtocol.UDP))
                yield return new(IPProtocol.UDP, Port);
        }
    }
}
