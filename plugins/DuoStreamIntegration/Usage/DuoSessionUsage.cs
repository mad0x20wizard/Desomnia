using MadWizard.Desomnia.Session;

namespace MadWizard.Desomnia.Service.Duo
{
    public class DuoSessionUsage(string name, string userName) : SessionUsage(userName, "Duo:" + (name != userName ? name : ""))
    {
        public string InstanceName => name;
    }
}
