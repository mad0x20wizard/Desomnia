using MadWizard.Desomnia.Network.Knocking.Secrets;
using System.Net;

namespace MadWizard.Desomnia.Network.Knocking
{
    public interface IKnockMethod
    {
        public void Knock(IPAddress source, IPEndPoint target, IPPort knock, SharedSecret secret);
    }
}
