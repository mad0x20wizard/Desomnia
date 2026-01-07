using MadWizard.Desomnia.Network.Demand;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct DemandOptions
    {
        public TimeSpan Timeout     { get; init; }
        public bool     Forward     { get; init; }
        public int      Parallel    { get; init; }

        public AddressAdvertisment Advertise { get; init; }

        public bool ShouldForward(DemandEvent @event) => Forward && @event.CanBeForwarded;

        public bool ShouldAdvertiseOnRemoteHostDemand(IPAddress ip)     => Advertise.HasFlag(AddressAdvertisment.Demand)    && ShouldAdvertise(ip);
        public bool ShouldAdvertiseOnRemoteHostSuspended(IPAddress ip)  => Advertise.HasFlag(AddressAdvertisment.Suspend)   && ShouldAdvertise(ip);
        public bool ShouldAdvertiseOnRemoteHostStopped(IPAddress ip)    => Advertise.HasFlag(AddressAdvertisment.Stop)      && ShouldAdvertise(ip);
        public bool ShouldAdvertiseOnLocalHostResume(IPAddress ip)      => Advertise.HasFlag(AddressAdvertisment.Resume)    && ShouldAdvertise(ip);

        private readonly bool ShouldAdvertise(IPAddress ip)
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork when Advertise.HasFlag(AddressAdvertisment.IPv4):
                    return true;
                case AddressFamily.InterNetworkV6 when Advertise.HasFlag(AddressAdvertisment.IPv6):
                    return true;

                default:
                    return false;
            }
        }
    }

    [Flags]
    public enum AddressAdvertisment
    {
        Never   = 0,

        IPv4    = 1 << 1,
        IPv6    = 1 << 2,

        Both    = IPv4 | IPv6,

        Demand  = 1 << 10, // advertise IPs when remote host is requested
        Suspend = 1 << 11, // advertise IPs after the remote host has been suspended
        Stop    = 1 << 12, // advertise IPs after the remote host has been stopped (manually or on disconnect)

        Resume  = 1 << 15, // advertise IPs when the local host resumes from suspend

        Lazy    = Both | Demand,
        Eager   = Both | Demand | Suspend | Resume
    }
}
