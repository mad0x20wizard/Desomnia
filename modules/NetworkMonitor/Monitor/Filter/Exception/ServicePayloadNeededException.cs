using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class ServicePayloadNeededException(IPPort port) : AdditionalDataNeededException
    {
        public IPPort Port { get; set; } = port;
    }
}
