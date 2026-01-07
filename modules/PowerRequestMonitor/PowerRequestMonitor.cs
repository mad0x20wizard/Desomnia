using Autofac;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.PowerRequest.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.PowerRequest
{
    public class PowerRequestMonitor(PowerRequestMonitorConfig config, IPowerManager power) : IInspectable, IStartable
    {
        public required ILogger<PowerRequestMonitor> Logger { get; set; }

        void IStartable.Start()
        {
            Logger.LogDebug("Startup complete");
        }

        IEnumerable<UsageToken> IInspectable.Inspect(TimeSpan interval)
        {
            var filteredRequests = power.Where(ShouldMonitorRequest);

            if (config.Request.Any())
            {
                foreach (var request in filteredRequests)
                    foreach (var info in config.Request)
                        if (Matches(request, info.Pattern))
                            yield return new PowerRequestToken(info.Name);
            }
            else if (filteredRequests.Any()) // if there aren't any requests configured, any power request will match
            {
                yield return new PowerRequestToken();
            }
        }

        private bool ShouldMonitorRequest(IPowerRequest request)
        {
            foreach (var filter in config.RequestFilter)
                if (Matches(request, filter.Pattern))
                    return false;

            return true;
        }

        private static bool Matches(IPowerRequest request, Regex pattern)
        {
            if (pattern.Matches(request.Name).Count > 0)
                return true;
            if (request.Reason != null && pattern.Matches(request.Reason).Count > 0)
                return true;

            return false;
        }
    }
}
