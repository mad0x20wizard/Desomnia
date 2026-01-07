using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class IPUnicastNeededException(IPAddress ip) : AdditionalDataNeededException
    {
        public IPAddress Address { get; set; } = ip;
    }
}
