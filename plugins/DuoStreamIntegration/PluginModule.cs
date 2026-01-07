using Autofac;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine;

namespace MadWizard.Desomnia.Service.Duo
{
    public class PluginModule : Desomnia.ConfigurableModule<DuoConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if (Config.DuoStreamMonitor is DuoStreamMonitorConfig configMonitor)
            {
                builder.RegisterType<DuoManager>()
                    .WithParameter(TypedParameter.From(configMonitor))
                    .AsImplementedInterfaces().AsSelf()
                    .SingleInstance();

                builder.RegisterType<DuoStreamMonitor>()
                    .WithParameter(TypedParameter.From(configMonitor))
                    .AsImplementedInterfaces().AsSelf()
                    .SingleInstance();

                builder.RegisterType<SunshineServiceWatch>()
                    .AsSelf();
                builder.RegisterType<SunshineServiceWatchFallback>()
                    .AsSelf();
            }



            //builder.RegisterType<DuoStreamUX>()
            //    .AsImplementedInterfaces()
            //    .SingleInstance();
        }
    }
}
    