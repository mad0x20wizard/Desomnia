using ConcurrentCollections;
using MadWizard.Desomnia.Network.Services;

namespace MadWizard.Desomnia.Network.Filter
{
    #region Traffic Types
    public interface ITrafficType;

    public readonly record struct ARPTrafficType : ITrafficType;
    public readonly record struct NDPTrafficType : ITrafficType;
    public readonly record struct WOLTrafficType : ITrafficType;

    public readonly record struct IPv4TrafficType : ITrafficType;
    public readonly record struct IPv6TrafficType : ITrafficType;

    public readonly record struct TCPTrafficType(ushort? Port, bool WithData = false) : ITrafficType;
    public readonly record struct UDPTrafficType(ushort? Port) : ITrafficType;

    public readonly record struct ICMPEchoTrafficType : ITrafficType;
    #endregion

    public class BerkeleyPacketFilter(NetworkDevice device) : INetworkService
    {
        private bool _shouldApplyFilter = false;

        readonly ConcurrentHashSet<TrafficFilterRequest> _requests = [];

        private IEnumerable<ITrafficType> Types => _requests.SelectMany(x => x.Types).Distinct();

        internal void AddRequest(TrafficFilterRequest request)
        {
            if (_requests.Add(request))
            {
                request.Disposed += (sender, args) =>
                {
                    if (_requests.TryRemove(request))
                    {
                        if (_shouldApplyFilter) ApplyFilters();
                    }
                };

                if (_shouldApplyFilter) ApplyFilters();
            }
        }

        void INetworkService.Startup()
        {
            _shouldApplyFilter = true;

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            string filter = $"(not ip and not ip6)";

            if (Types.OfType<IPv4TrafficType>().Any())
            {
                var ip = "ip";

                if (Types.OfType<TCPTrafficType>().Any() && Types.OfType<TCPTrafficType>().All(type => !type.WithData))
                {
                    ip += " and (not tcp or (tcp[tcpflags] & tcp-syn != 0))"; // only SYN packets or no TCP at all
                }

                if (!Types.OfType<UDPTrafficType>().Any())
                {
                    ip += " and not udp";
                }

                filter += $" or ({ip})";
            }

            if (Types.OfType<IPv6TrafficType>().Any()) // BPF cannot use symbols for any protocol higher than IPv6
            {
                var ip6 = "ip6";

                if (Types.OfType<TCPTrafficType>().Any() && Types.OfType<TCPTrafficType>().All(type => !type.WithData))
                {
                    ip6 += " and (ip6[6] != 6 or (ip6[53] & 0x02 != 0))";
                }

                if (!Types.OfType<UDPTrafficType>().Any())
                {
                    ip6 += " and ip6[6] != 17";
                }

                filter += $" or ({ip6})";
            }

            device.Filter = filter;
        }

        void INetworkService.Shutdown()
        {
            _shouldApplyFilter = false;

            device.Filter = "";
        }
    }

    public class TrafficFilterRequest : IDisposable
    {
        public ITrafficType[] Types { get; private init; }

        public event EventHandler? Disposed;

        private bool _disposed;

        public TrafficFilterRequest(ITrafficType[] types, BerkeleyPacketFilter? filter = null)
        {
            Types = types;

            filter?.AddRequest(this);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disposed?.Invoke(this, EventArgs.Empty);

                _disposed = true;
            }
        }
    }
}
