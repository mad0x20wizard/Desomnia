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

                foreach (var filter in instance.Service.CreateFilterRules(instance.Info))
                {
                    RegisterServiceFilter(builder, filter);
                }

                var watch = builder.RegisterType<ServiceFilterWatch>().As<NetworkServiceWatch>()
                    //.WithProperty(TypedParameter.From(info.MakeAdvertiseOptions())) // TODO MakeAdvertiseOptions ??
                    .SingleInstance()
                    .AsSelf();

                watch.OnActivated(args =>
                {
                    args.Instance.Threshold = instance.Info.MinStreamTraffic;
                });
            });
        }
    }
}
