using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using Microsoft.Extensions.Logging;
using NetTools;
using System.Net;

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
                if (range is DynamicHostRangeInfo dynamicRange)
                {
                    // register dynamic ranges
                    var reg = builder.RegisterType<NetworkServiceHostRange>()
                        .Named<NetworkHostRange>(range.Name)
                        .SingleInstance()
                        .AsSelf();

                    // add static service filters
                    reg.WithProperty(TypedParameter.From<IEnumerable<ServiceFilterRule>>(
                        [.. CreateStaticFilterRules(dynamicRange.ServiceFilterRule)]
                    ));
                }
                else
                {
                    // register static ranges
                    builder.RegisterType<NetworkHostRange>()
                        .Named<NetworkHostRange>(range.Name)
                        .SingleInstance()
                        .AsSelf();
                }
            }
        }

        private static IEnumerable<ServiceFilterRule> CreateStaticFilterRules(IEnumerable<ServiceFilterRuleInfo> rules)
        {
            foreach (var filter in rules)
            {
                // Only protocol, port and rule type apply here; no nested host or payload filters.
                if (filter.Protocol.HasFlag(IPProtocol.TCP))
                    yield return new TCPServiceFilterRule(filter.Port) { Type = filter.Type };
                if (filter.Protocol.HasFlag(IPProtocol.UDP))
                    yield return new UDPServiceFilterRule(filter.Port) { Type = filter.Type };
            }
        }

        internal async Task DiscoverHostRanges()
        {
            Logger.LogDebug("Discovering network host ranges...");

            var janitor = Scope.Resolve<NetworkJanitor>();

            foreach (var config in Config.Ranges)
            {
                var range = Scope.ResolveNamed<NetworkHostRange>(config.Name);

                foreach (IPAddressRange address in config)
                {
                    range.AddAddressRange(address, new IPAddressRangeOptions(IPAddressRangeFlags.Static)); // add static ranges
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

                janitor.WatchRange(range);
            }
        }

        internal NetworkKnockContext CreateKnockContext(DynamicHostRangeInfo config, NetworkHostRange range)
        {
            var context = Scope.Resolve<NetworkKnockContext>(TypedParameter.From(range), TypedParameter.From(config));

            try
            {
                Scope.Resolve<NetworkJanitor>().WatchRange(range);

                if (context.ToArray() is { } stanzas) // disposal must not resolve from the ending scope
                {
                    var knocking = Scope.Resolve<KnockService>();

                    context.Scope.CurrentScopeEnding += (sender, args) =>
                    {
                        foreach (var stanza in stanzas)
                        {
                            knocking.RemoveStanza(stanza);
                        }

                        _knockContexts.Remove(context);

                        Logger.LogDebug("Removed dynamic host range '{Name}'", config.Name);
                    };

                    foreach (var stanza in stanzas)
                    {
                        knocking.AddStanza(stanza);
                    }

                    _knockContexts.Add(context);

                    Logger.LogDebug("Created dynamic host range '{Name}'", config.Name);
                }

                return context;
            }
            catch
            {
                context.Dispose(); throw;
            }
        }
    }
}
