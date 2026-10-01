using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class InstanceSessionChangedEventArgs(DuoInstance instance, ISession? session) : EventArgs
    {
        public DuoInstance Instance { get; } = instance;

        public ISession? Session { get; } = session;

        public bool IsRunning => Session != null;

        public bool Manually { get; set; } = true;
    }
}
