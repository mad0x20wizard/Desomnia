using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using NetTools;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        private void RegisterHostRanges(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            builder.RegisterType<LocalNetworkRange>()
                .SingleInstance()
                .AsSelf();

            foreach (var range in config.Ranges)
            {
                // register static ranges
                builder.RegisterType<NetworkHostRange>()
                    .Named<NetworkHostRange>(range.Name)
                    .SingleInstance()
                    .AsSelf();
            }
        }

        internal async Task DiscoverHostRanges()
        {
            Logger.LogDebug("Discovering network host ranges...");

            foreach (var config in Config.Ranges)
            {
                var range = Scope.ResolveNamed<NetworkHostRange>(config.Name);

                foreach (IPAddressRange address in config)
                {
                    range.AddAddressRange(address); // add static ranges
                }

                foreach (var host in this.FindHostsByName(config.Host.Select(c => c.Name)))
                {
                    range.AddHostAddressRange(host); // add dynamic host ranges
                }

                if (config is DynamicHostRangeInfo configDynamic)
                {
                    try
                    {
                        CreateKnockContext(configDynamic, range);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"DynamicHostRange '{configDynamic.Name}' could not be initialized.");
                    }
                }
                else
                {
                    Logger.LogDebug("Created host range '{Name}'", config.Name);
                }
            }
        }

        internal NetworkKnockContext CreateKnockContext(DynamicHostRangeInfo config, NetworkHostRange range)
        {
            var context = Scope.Resolve<NetworkKnockContext>(TypedParameter.From(range), TypedParameter.From(config));

            Logger.LogDebug("Created dynamic host range '{Name}'", config.Name);

            _knockContexts.Add(context);

            context.Scope.CurrentScopeEnding += (sender, args) =>
            {
                Logger.LogDebug("Removed dynamic host range '{Name}'", config.Name);

                _knockContexts.Remove(context);
            };

            return context;
        }
    }
}
