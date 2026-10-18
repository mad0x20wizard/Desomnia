using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class ServicePayloadNeededException(IPService port) : AdditionalDataNeededException
    {
        public IPService Port { get; set; } = port;
    }
}
