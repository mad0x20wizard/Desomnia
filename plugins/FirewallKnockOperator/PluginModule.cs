using Autofac;
using MadWizard.Desomnia.Network.Knocking;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    public class PluginModule : Desomnia.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Implement Firewall Knock Operator (FKO) protocol
            builder.RegisterType<FKOReceiver>()
                .Named<IKnockDetector>("fko")
                .AsImplementedInterfaces()
                .SingleInstance();
            builder.RegisterType<FKOSender>()
                .Named<IKnockMethod>("fko")
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }
}
