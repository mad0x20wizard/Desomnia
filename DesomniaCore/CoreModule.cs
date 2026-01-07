using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Logging;
using MadWizard.Desomnia.Power;
using NLog;

namespace MadWizard.Desomnia
{
    public class CoreModule : Desomnia.ConfigurableModule<SystemMonitorConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if ((Config.Version) < SystemMonitorConfig.MIN_VERSION || (Config.Version) > SystemMonitorConfig.MAX_VERSION)
                throw new NotSupportedException($"Unsupported configuration version = {Config.Version}");

            builder.RegisterType<ActionManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<SleepWatch>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<SystemMonitor>().As<IStartable>()
                .WithParameter(TypedParameter.From(Config))
                .SingleInstance()
                .AsSelf();

            if (Config.Timeout is TimeSpan interval)
            {
                builder.RegisterType<SystemUsageInspector>()
                    .WithParameter(TypedParameter.From(interval))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }

            builder.RegisterType<AsyncExceptionLogger>()
                .AsImplementedInterfaces()
                .SingleInstance();

        }
    }
}
