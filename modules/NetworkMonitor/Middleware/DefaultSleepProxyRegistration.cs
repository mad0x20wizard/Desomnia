using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Handoff.Registration;
using MadWizard.Desomnia.Network.Monitor.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Watch;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class DefaultSleepProxyRegistration : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            if (context.FirstParameterOfType<LocalHostWatch>() is LocalHostWatch watch)
            {
                context.ChangeParameters([ ..context.Parameters,
                    TypedParameter.From(watch.Host),
                    TypedParameter.From(watch.HandoffOptions),
                    TypedParameter.From(watch.SleepProxyRegistrationCycle)
                ]); next(context);

                if (context.Instance is SleepProxyRegistration reg)
                {
                    foreach (var watchService in watch.Where(w => w.ShouldHandoffToSleepProxy))
                    {
                        if (watchService.Service is not TransportNetworkService service)
                            continue;

                        var info = new HandoffServiceInfo(watch.AdvertiseOptions)
                        {
                            Name = service.Name,
                            ServiceName = service.ServiceName,
                            InstanceName = service.InstanceName,

                            Protocol = service.Port.Protocol,
                            Port = service.Port,

                            Priority = service.Priority,
                            Weight = service.Weight,

                            Properties = service.Properties,
                        };

                        if (watchService is ServiceFilterWatch filterWatch)
                        {
                            ExtractFlattenFilterRules(info, filterWatch.Filter.Value.Rules);
                        }

                        reg.Services.Add(info);
                    }
                }
            }
        }

        private static void ExtractFlattenFilterRules(HandoffServiceInfo info, IEnumerable<PacketFilterRule> rules)
        {
            // first we map the direct host filter rules of the service, these are easy
            foreach (var rule in rules.OfType<TransportFilterRule>().Where(r => r.Type == FilterRuleType.Must && r.Port == info.Port).SelectMany(r => r.HostRules))
            {
                switch (rule)
                {
                    case StaticHostFilterRule ruleStatic:
                        foreach (var ip in ruleStatic.Addresses)
                            info.HostFilterRule.Add(new(ip) { Type = ruleStatic.Type });
                        break;

                    case DynamicHostFilterRule ruleDynamic:
                        if (ruleDynamic.Host.Name != ruleDynamic.Host.HostName)
                            throw new NotSupportedException("DynamicHostFilterRule cannot have mismatched hostname");
                        info.HostFilterRule.Add(new(ruleDynamic.Host.Name) { Type = ruleDynamic.Type });
                        break;

                    case StaticHostRangeFilterRule ruleRangeStatic:
                        info.HostRangeFilterRule.Add(new(ruleRangeStatic.Range) { Type = ruleRangeStatic.Type });
                        break;

                    case DynamicHostRangeFilterRule:
                        throw new NotSupportedException("DynamicHostRangeFilterRule ist not supported");

                    default:
                        throw new NotSupportedException($"{rule.GetType().Name} is not supported");
                }
            }

            // then we try to map what we can, of the general host filter rules
            foreach (var rule in rules.OfType<HostFilterRule>().Where(r => r.Type == FilterRuleType.MustNot))
            {
                switch (rule)
                {
                    case StaticHostFilterRule ruleStatic:
                        foreach (var ip in ruleStatic.Addresses)
                            info.HostFilterRule.Add(new(ip));
                        break;

                    case DynamicHostFilterRule ruleDynamic:
                        if (ruleDynamic.Host.Name != ruleDynamic.Host.HostName)
                            throw new NotSupportedException("DynamicHostFilterRule cannot have mismatched hostname");
                        info.HostFilterRule.Add(new(ruleDynamic.Host.Name));
                        break;

                    case StaticHostRangeFilterRule ruleRangeStatic:
                        info.HostRangeFilterRule.Add(new(ruleRangeStatic.Range));
                        break;

                    case DynamicHostRangeFilterRule:
                        throw new NotSupportedException("DynamicHostRangeFilterRule ist not supported");
                }
            }

            // then we try to map some of the edge cases
            foreach (var ruleEveryHost in rules.OfType<EveryHostFilterRule>().Where(r => r.Type == FilterRuleType.MustNot))
            {
                if (ruleEveryHost is IPFilterRule)
                    continue;

                if (ruleEveryHost is ForeignHostFilterRule)
                {
                    info.HostRangeFilterRule.Add(new LocalRangeFilterRuleInfo() { Type = FilterRuleType.Must });
                }

                foreach (var ruleHost in ruleEveryHost.HostRules.Where(r => r.Type == FilterRuleType.MustNot))
                {
                    switch (ruleHost)
                    {
                        case StaticHostFilterRule ruleStatic:
                            foreach (var ip in ruleStatic.Addresses)
                                info.HostFilterRule.Add(new(ip) { Type = FilterRuleType.Must });
                            break;

                        case DynamicHostFilterRule ruleDynamic when ruleDynamic.Host is NetworkHost host:
                            if (host.Name != host.HostName)
                                throw new NotSupportedException($"DynamicHostFilterRule: host {host.Name} cannot have mismatched hostname {host.HostName}");
                            info.HostFilterRule.Add(new(host.Name) { Type = FilterRuleType.Must });
                            break;

                        case StaticHostRangeFilterRule ruleRangeStatic:
                            info.HostRangeFilterRule.Add(new(ruleRangeStatic.Range) { Type = FilterRuleType.Must });
                            break;

                        case DynamicHostRangeFilterRule:
                            throw new NotSupportedException("DynamicHostRangeFilterRule ist not supported");
                    }
                }
            }
        }
    }
}
