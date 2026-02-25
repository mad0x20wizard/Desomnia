using Autofac;
using MadWizard.Desomnia.Network.Knocking;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    public class PluginModule : Desomnia.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<Receiver>()
                .Named<IKnockDetector>("fko")
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<Sender>()
                .Named<IKnockMethod>("fko")
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }
}
