using Autofac;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Neighborhood;
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
                    if (configChildHost.Name is not null)
                    {
                        // TODO add IPs and sync with auto configuration
                    }
                    else
                    {
                        foreach (var ip in configChildHost.IPAddresses)
                        {
                            range.AddAddressRange(new IPAddressRange(ip));
                        }
                    }
                }

                if (configRange is DynamicHostRangeInfo dynamic)
                {
                    CreateKnockContext(dynamic);
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
