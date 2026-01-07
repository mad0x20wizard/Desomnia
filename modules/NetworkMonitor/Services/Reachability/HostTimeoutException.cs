namespace MadWizard.Desomnia.Network.Reachability
{
    public class HostTimeoutException(TimeSpan timeout) : TimeoutException
    {
        public TimeSpan Timeout => timeout;
    }
}
