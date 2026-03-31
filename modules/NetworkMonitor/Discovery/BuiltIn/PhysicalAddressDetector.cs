using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Extensions;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal abstract class PhysicalAddressDetector<T> : IPhysicalAddressDiscovery
    {
        public abstract ILogger<T> Logger { protected get; init; }

        public required NetworkDevice   Device { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        public required ReachabilityService Reachability { protected get; init; }

        public required DiscoveryOptions Options { private get; init; }

        async Task IPhysicalAddressDiscovery.DiscoverAddress(NetworkHost host)
        {
            var ips = host.IPAddresses.Where(ip => ip.AddressFamily == Family && Network.LocalRange.Contains(ip));

            if (host.PhysicalAddress is null && ips.Any())
            {
                using SemaphoreSlim semaphore = new(0);

                Logger.LogDebug("Host '{HostName}' has no MAC address configured, try to resolve dynamically...", host.Name);

                async void Capture(object? sender, EthernetPacket packet)
                {
                    if (AnalyzePacket(packet) is AddressMapping mapping)
                    {
                        if (ips.Contains(mapping.IPAddress) && host.PhysicalAddress is null)
                        {
                            host.PhysicalAddress = mapping.PhysicalAddress;

                            Logger.LogHostPhysicalAddressChanged(host, mapping.PhysicalAddress);

                            semaphore.MaybeRelease();
                        }
                    }
                }

                Device.EthernetCaptured += Capture;

                try
                {
                    foreach (var ip in ips)
                    {
                        if (semaphore.CurrentCount == 0) // have we already received a response?
                            SendRequest(ip);
                    }

                    await semaphore.WaitAsync(Options.Timeout);
                }
                finally
                {
                    Device.EthernetCaptured -= Capture;
                }
            }
        }

        protected abstract AddressFamily Family { get; }

        protected abstract void SendRequest(IPAddress ip);

        protected abstract AddressMapping? AnalyzePacket(EthernetPacket packet);

    }
}
