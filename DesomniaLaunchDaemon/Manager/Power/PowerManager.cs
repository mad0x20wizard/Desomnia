using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Power.Manager
{
    public class PowerManager : IPowerManager
    {
        public required ILogger<PowerManager> Logger { private get; init; }

        public event EventHandler? Suspended;
        public event EventHandler? ResumeSuspended;

        public void Suspend(bool hibernate = false)
        {
            throw new NotImplementedException("Suspend");
        }

        public void Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            throw new NotImplementedException("Shutdown");
        }

        public void Reboot(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            throw new NotImplementedException("Reboot");
        }

        IPowerRequest IPowerManager.CreateRequest(string reason)
        {
            throw new NotImplementedException("CreatePowerRequest");
        }

        IEnumerator<IPowerRequest> IEnumerable<IPowerRequest>.GetEnumerator()
        {
            yield break; // TODO: implement PowerRequests enumeration
        }
    }
}
