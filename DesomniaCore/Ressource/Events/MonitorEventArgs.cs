namespace MadWizard.Desomnia.Ressource.Events
{
    public class MonitorEventArgs(ResourceMonitor monitor) : EventArgs
    {
        public bool IsMonitoredBy<T>(out T output) where T : ResourceMonitor
        {
            output = (monitor as T)!;

            return monitor.GetType().IsAssignableTo(typeof(T));
        }
    }
}
