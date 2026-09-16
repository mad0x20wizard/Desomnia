using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Watch;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SunshineServiceContext : NetworkServiceContext
    {
        public SunshineServiceContext(ILifetimeScope parent, SunshineService service, TransmissionThreshold? threshold) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkServiceLifetimeScopeTag, builder =>
            {
                RegisterService(builder, service);

                var watch = builder.RegisterType<ServiceFilterWatch>().As<NetworkServiceWatch>()
                    //.WithProperty(TypedParameter.From(info.MakeAdvertiseOptions())) // TODO MakeAdvertiseOptions ??
                    .SingleInstance()
                    .AsSelf();

                watch.OnActivated(args =>
                {
                    args.Instance.Threshold = threshold;
                });
            });
        }
    }
}
