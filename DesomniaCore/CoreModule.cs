using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Environments.Conditions;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Logging;
using MadWizard.Desomnia.Power.Guard;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Power.Source;
using MadWizard.Desomnia.Power.Watch;
using NLog;
using NLog.Config;

namespace MadWizard.Desomnia
{
    public class CoreModule : Desomnia.ConfigurableModule<SystemMonitorConfig>
    {
        protected internal override void ConfigureLogging(ISetupExtensionsBuilder builder)
        {
            builder.RegisterLayoutRenderer<SleepTimeLayoutRenderer>("sleep-duration");
        }

        protected internal override void LoadOnce(ContainerBuilder builder)
        {
            // conditions on the process's environment variables (xmlns:env="environment:process");
            // PreserveExistingDefaults so a platform module (which loads first) could take it over
            builder.RegisterType<ProcessEnvironmentConditionProvider>()
                .Named<IEnvironmentConditionProvider>(ProcessEnvironmentConditionProvider.Namespace)
                .PreserveExistingDefaults();

            builder.RegisterType<PowerSourceCondition>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(IPowerSource))))
                .Named<IEnvironmentCondition>("power");
        }

        protected override void Load(ContainerBuilder builder, SystemMonitorConfig config)
        {
            builder.RegisterServiceMiddlewareSource(new EventSystemMiddlewareSource());

            builder.RegisterType<ActionManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterDecorator<GuardedPowerManager, IPowerManager>();

            builder.RegisterType<SleepWatch>()
                .AsImplementedInterfaces()
                .SingleInstance();
            builder.RegisterType<StartupWatch>()
                .AsImplementedInterfaces()
                .SingleInstance();
            builder.RegisterType<ShutdownWatch>()
                .AsImplementedInterfaces()
                .SingleInstance();


            builder.RegisterType<SystemMonitor>().As<IStartable>()
                .WithParameter(TypedParameter.From(config))
                .SingleInstance()
                .AsSelf();

            if (config.Timeout is TimeSpan interval)
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
