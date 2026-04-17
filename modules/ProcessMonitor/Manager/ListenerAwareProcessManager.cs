namespace MadWizard.Desomnia.Process.Manager
{
    public abstract class ListenerAwareProcessManager : ProcessManager
    {
        protected int ListenerCount
        {
            get; set
            {
                field = value;

                ListenerCountChanged?.Invoke(this, field);
            }
        } = 0;

        protected event EventHandler<int>? ListenerCountChanged;

        public override event EventHandler<IProcess>? ProcessStarted
        {
            add     { base.ProcessStarted += value; ListenerCount++; }
            remove  { base.ProcessStarted -= value; ListenerCount--; }
        }

        public override event EventHandler<IProcess>? ProcessStopped
        {
            add     { base.ProcessStopped += value; ListenerCount++; }
            remove  { base.ProcessStopped -= value; ListenerCount--; }
        }
    }
}
