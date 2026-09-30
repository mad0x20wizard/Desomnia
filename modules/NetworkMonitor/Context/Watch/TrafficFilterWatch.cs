using Autofac;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using System.Net;

namespace MadWizard.Desomnia.Network.Context.Watch
{
    /**
     * We only need to monitor traffic for hosts with known IP addresses
     * and only for those IP address families, by which it is reachable.
     */
    internal class TrafficFilterWatch(ILifetimeScope scope, AutoDiscoveryType auto) : IDisposable
    {
        private TrafficFilterRequest? _requestIPv4;
        private TrafficFilterRequest? _requestIPv6;

        public required NetworkHost Host
        {
            get; init
            {
                field = value;

                field.AddressAdded += Host_AddressAdded;
                field.AddressRemoved += Host_AddressRemoved;

                ConfigureTrafficFilters();
            }
        }

        private void Host_AddressAdded(object? sender, AddressAddedEventArgs e)
        {
            ConfigureTrafficFilters();
        }

        private void Host_AddressRemoved(object? sender, AddressRemovedEventArgs e)
        {
            ConfigureTrafficFilters();
        }

        internal void ConfigureTrafficFilters()
        {
            void UpdateFilter(ref TrafficFilterRequest? request, AutoDiscoveryType type, IEnumerable<IPAddress> addresses)
            {
                var needed = auto.HasFlag(type) || addresses.Any();

                switch (request is not null)
                {
                    case true when !needed:
                        request.Dispose();
                        request = null;
                        break;

                    case false when needed:
                        request = scope.UseTrafficType(type switch
                        {
                            AutoDiscoveryType.IPv4 => new IPv4TrafficType(),
                            AutoDiscoveryType.IPv6 => new IPv6TrafficType(),
                            _ => throw new NotImplementedException()
                        }); break;
                }
            }

            lock (this)
            {
                UpdateFilter(ref _requestIPv4, AutoDiscoveryType.IPv4, Host.IPv4Addresses);
                UpdateFilter(ref _requestIPv6, AutoDiscoveryType.IPv6, Host.IPv6Addresses);
            }
        }

        void IDisposable.Dispose()
        {
            Host.AddressRemoved -= Host_AddressRemoved;
            Host.AddressAdded -= Host_AddressAdded;
        }
    }
}
