using Autofac;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using NetTools;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        internal async Task DiscoverHostRanges()
        {
            foreach (var configRange in Config.Ranges)
            {
                var range = Scope.ResolveNamed<NetworkHostRange>(configRange.Name);

                if (configRange.AddressRange is IPAddressRange addressRange)
                    range.AddAddressRange(addressRange);

                foreach (var configChildRange in configRange.HostRange)
                    if (configChildRange.AddressRange is IPAddressRange childAddressRange)
                        range.AddAddressRange(childAddressRange);

                foreach (var configChildHost in configRange.Host)
                {
                    var ips = configChildHost.IPAddresses;

                    if (configChildHost.Name is not null)
                    {
                        var ctx = _hostContexts.First(ctx => ctx.Host.Name == configChildHost.Name);

                        ctx.Host.AddressAdded += (sender, @event) => range.AddAddress(@event.IPAddress);
                        ctx.Host.AddressRemoved += (sender, @event) => range.RemoveAddress(@event.IPAddress);

                        ips = ctx.Host.IPAddresses;
                    }

                    foreach (var ip in ips)
                    {
                        range.AddAddressRange(new IPAddressRange(ip));
                    }
                }

                if (configRange is DynamicHostRangeInfo dynamic)
                {
                    try
                    {
                        CreateKnockContext(dynamic);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"DynamicHostRange '{dynamic.Name}' could not be initialized.");
                    }
                }
            }
        }

        private void CreateKnockContext(DynamicHostRangeInfo range)
        {
            var context = Scope.Resolve<NetworkKnockContext>(TypedParameter.From(range));

            _knockContexts.Add(context);
        }
    }
}
