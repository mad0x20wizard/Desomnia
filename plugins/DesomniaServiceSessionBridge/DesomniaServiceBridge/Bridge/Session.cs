using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Pipe.Config;
using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Service.Bridge.Minion;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Bridge
{
    internal class Session : TerminalServicesSession
    {
        DateTime? _lastInputTime;

        internal SessionMinion? Minion { get; private set; }

        internal SessionMinion LaunchMinion(MinionConfig config)
        {
            Minion = new SessionMinion(config, this);
            {
                Minion.MessageReceived += Minon_MessageReceived;
                Minion.Terminated += Minion_Terminated;
            }

            return Minion;
        }

        private void Minon_MessageReceived(object? sender, UserMessage message)
        {
            switch (message)
            {
                case InputTimeMessage input:
                    _lastInputTime = input.LastInputTime;
                    break;
            }
        }

        private void Minion_Terminated(object? sender, EventArgs e)
        {
            Minion = null;
        }

        internal SessionMatcher SessionControl { get; set; } = SessionMatcher.None;
        internal bool PowerControl { get; set; } = false;

        public override DateTime? LastInputTime { get => _lastInputTime ?? base.LastInputTime; }

        public override async Task Lock()
        {
            if (Minion != null)
            {
                Minion.Send(new LockMessage());
            }
            else
            {
                throw new NotSupportedException("Minion not available");
            }
        }

        public override void Dispose()
        {
            if (Minion is not null)
            {
                Minion.Dispose();

                Minion = null;
            }

            base.Dispose();
        }
    }
}
