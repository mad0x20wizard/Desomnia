using MadWizard.Desomnia.Session;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoSessionUsage(string name, string userName) : SessionUsage(userName, "Duo:" + (name != userName ? name : ""))
    {

    }
}
