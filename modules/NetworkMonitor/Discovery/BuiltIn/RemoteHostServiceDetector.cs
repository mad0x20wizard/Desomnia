using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class RemoteHostServiceDetector : IWatchedServiceDiscovery
    {
        public required ILogger<RemoteHostServiceDetector> Logger { private get; init; }

        public required ReachabilityService Reachability { private get; init; }

        async Task IWatchedServiceDiscovery.DiscoverServices(NetworkHostWatch watch)
        {
            switch (watch)
            {
                /**
                 * If the user specified to detect services of remote hosts,
                 * the watched services are usually advertised to the Sleep Proxy,
                 * when the host suspends.
                 * 
                 * So in order to detect the services of an already sleeping host,
                 * we have to wake it once.
                 */
                case RemoteHostWatch remote when remote.Host is not VirtualNetworkHost:
                    Logger.LogDebug("Awaiting services for '{Host}' dynamically", remote.Host.Name);

                    using (Logger.BeginHostScope(remote.Host))
                    {
                        if (!(await Reachability.Test(remote, label: "dynamic remote host")))
                            if (remote.Host.PhysicalAddress is not null)
                            {
                                Logger.LogInformation("Remote host '{Host}' is not reachable. " +
                                    "Waking it up now, so it can advertise its services.", watch.Host.Name);

                                try
                                {
                                    await remote.WakeUp();
                                }
                                catch (HostTimeoutException ex)
                                {
                                    Logger.LogWarning("Remote host '{Host}' didn't wake up after {Timeout} s",
                                        watch.Host.Name, Math.Ceiling(ex.Timeout.TotalSeconds));
                                }
                            }
                            else
                            {
                                Logger.LogWarning("Remote host '{Host}' is not reachable. " +
                                    "Cannot wake up, in order to detect services, since it has no MAC address configured.", watch.Host.Name);
                            }
                    }
                    break;
            }
        }
    }
}
