using Autofac;
using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Firewall.WindowsFirewall.Manager;

namespace MadWizard.Desomnia.Firewall.WindowsFirewall
{
    public class PluginModule : Desomnia.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<WindowsFirewallManager>()
                .As<IFirewallManager>()
                .SingleInstance();
        }
    }
}
