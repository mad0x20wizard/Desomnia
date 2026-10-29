using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Firewall
{
    /// <summary>Startup-only prototype. Resource changes are deliberately not subscribed.</summary>
    public sealed class FirewallMonitor : INetworkService, IDisposable
    {
        public required NetworkMonitor Monitor { private get; init; }
        public required IFirewallManager Firewall { private get; init; }
        public required ILogger<FirewallMonitor> Logger { private get; init; }

        private readonly List<IDisposable> _policies = [];

        Task INetworkService.AfterStartup()
        {
            if (_policies.Count != 0)
                return Task.CompletedTask;

            // Compile the entire snapshot before installing anything.
            var policies = Monitor.TakeSnapshot()
                .OfType<HostDemandWatch>()
                .Where(w => w.Host is LocalHost) // LocalVirtualHostWatch also derives from LocalHostWatch.
                .Select(w => (w.Host, Rules: w.TakeSnapshot().OfType<NetworkServiceWatch>()
                    .Where(s => w.Host[s.Service].HasFlags(ServiceFlags.Static))
                    .SelectMany(s => s.Service is NetworkService service
                        ? service.SelectMany(port => FirewallRuleCompiler.Compile(
                            w.Host.IPAddresses, port, s.Filter.Value.Rules))
                        : throw new NotSupportedException($"Firewall prototype cannot project service '{s.Service}'."))
                    .Distinct().ToArray()))
                .ToArray();

            try
            {
                foreach (var (host, rules) in policies)
                {
                    if (rules.Length == 0)
                        continue;

                    _policies.Add(Firewall.Install(host, rules));
                    Logger.LogInformation("Installed {Count} firewall rules for '{Host}'", rules.Length, host.Name);
                }
            }
            catch
            {
                Dispose();
                throw;
            }

            return Task.CompletedTask;
        }

        Task INetworkService.Shutdown(NetworkShutdownReason reason)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var policy in _policies)
                policy.Dispose();
            _policies.Clear();
        }
    }
}
