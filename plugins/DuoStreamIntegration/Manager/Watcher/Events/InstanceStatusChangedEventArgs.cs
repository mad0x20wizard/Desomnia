namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class InstanceStatusChangedEventArgs(DuoInstance instance, bool status) : EventArgs
    {
        public DuoInstance Instance { get; } = instance;

        public bool Manually { get; set; } = true;

        public bool Status { get; } = status;
    }
}
