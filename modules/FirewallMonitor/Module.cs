using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Network;

namespace MadWizard.Desomnia.Firewall
{
    public class Module : Desomnia.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<FirewallMonitor>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(IFirewallManager))))
                .As<INetworkService>()
                .InstancePerNetwork()
                .AsSelf();
        }
    }
}
