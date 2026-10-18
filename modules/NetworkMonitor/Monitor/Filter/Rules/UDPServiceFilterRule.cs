using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class UDPServiceFilterRule(ushort port) : ServiceFilterRule(new IPService(IPProtocol.UDP, port))
    {

    }
}
