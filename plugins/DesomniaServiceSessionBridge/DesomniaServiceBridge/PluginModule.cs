using Autofac;
using Autofac.Core;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Service.Bridge.Configuration;
using MadWizard.Desomnia.Service.Bridge.Notification;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Bridge
{
    public class PluginModule : Desomnia.ConfigurableModule<BridgeConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<NotificationAreaController>()
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();

            builder.RegisterType<SessionBridge>()
                .WithParameter(TypedParameter.From(Config))
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();

            builder.RegisterType<Session>().As<TerminalServicesSession>().As<ISession>()
                .ConfigurePipeline(p => p.Use(PipelinePhase.Activation, (context, next) =>
                {
                    next(context);

                    var bridge = context.Resolve<SessionBridge>();

                    if (context.Instance is Session session)
                    {
                        bridge.ConfigureSession(session);
                    }

                }));

            builder.RegisterType<InspectionController>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(SystemUsageInspector))))
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();
        }
    }
}
