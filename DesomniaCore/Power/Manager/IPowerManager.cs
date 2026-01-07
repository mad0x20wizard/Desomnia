namespace MadWizard.Desomnia.Power.Manager
{
    public interface IPowerManager : IIEnumerable<IPowerRequest>
    {
        public event EventHandler Suspended;
        public event EventHandler ResumeSuspended;

        public void Suspend(bool hibernate = false);

        public void Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false);
        public void Reboot(TimeSpan? timeout = null, string? message = null, bool force = false);

        public IPowerRequest CreateRequest(string reason);
    }
}
