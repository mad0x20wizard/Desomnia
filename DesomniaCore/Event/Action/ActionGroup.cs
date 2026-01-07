using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia
{
    class ActionGroup(ActionGroupConfig config) : Actor
    {
        protected override async Task<bool> HandleEventAction(Event eventObj, NamedAction action)
        {
            return false; // TODO
        }
    }
}
