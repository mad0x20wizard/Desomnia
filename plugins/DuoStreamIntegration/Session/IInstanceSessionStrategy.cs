using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Session
{
    public interface IInstanceSessionStrategy
    {
        bool this[DuoInstance instance, ISession session] { get; }
    }
}
