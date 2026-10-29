using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SunshineServiceContext : NetworkServiceContext
    {
        public SunshineServiceContext(ILifetimeScope parent, DuoInstance instance) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkServiceLifetimeScopeTag, builder =>
            {
                builder.RegisterInstance(instance.Service).As<NetworkService>();

                var watch = builder.RegisterType<NetworkServiceWatch>()
                    .WithProperty(TypedParameter.From(instance.Info.MinStreamTraffic))
                    //.WithProperty(TypedParameter.From(info.MakeAdvertiseOptions())) // TODO MakeAdvertiseOptions ??
                    .SingleInstance()
                    .AsSelf();

                RegisterHostFilters(builder, instance.Info.HostFilterRule);
                RegisterHostRangeFilters(builder, instance.Info.HostRangeFilterRule);

                RegisterServiceTraffic(builder, instance.Service);
            });
        }
    }
}
