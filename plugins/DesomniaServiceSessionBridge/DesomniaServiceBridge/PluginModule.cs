using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Service.Bridge.Configuration;
using MadWizard.Desomnia.Service.Bridge.Notification;
using MadWizard.Desomnia.Session.Manager.Bridged;

namespace MadWizard.Desomnia.Service.Bridge
{
    public class PluginModule : Desomnia.ConfigurableModule<BridgeConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<NotificationAreaController>()
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();

            builder.RegisterType<BridgedTerminalServicesManager>()
                .WithParameter(TypedParameter.From(Config))
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();

            builder.RegisterType<InspectionController>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(SystemUsageInspector))))
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();
        }
    }
}
