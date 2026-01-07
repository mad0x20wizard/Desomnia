using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class UDPServiceFilterRule(ushort port) : TransportFilterRule(new IPPort(IPProtocol.UDP, port))
    {

    }
}
