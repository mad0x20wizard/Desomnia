using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Actions
{
    class TerminalServicesBroadcaster : Actor
    {
        public required TerminalServicesManager Manager { private get; init; }

        [ActionHandler("message")]
        internal void HandleActionMessage(string str1, string? str2 = null)
        {
            string title, text;

            if (str2 != null)
            {
                title = str1;
                text = str2;
            }
            else
            {
                title = "Desomnia";
                text = str1;
            }

            foreach (var session in Manager)
            {
                if (session is TerminalServicesSession ts)
                {
                    ts.SendMessage(title, text);
                }
            }
        }
    }
}