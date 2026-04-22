using Autofac;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using WindowsFirewallHelper;

namespace MadWizard.Desomnia.Service.Duo
{
    public class PluginModule : Desomnia.ConfigurableModule<DuoConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if (Config.DuoStreamMonitor is DuoStreamMonitorConfig configMonitor)
            {
                // Since you're on a Windows Service (Vista+), use FirewallWAS directly
                // for the most control. It targets the "Windows Firewall with Advanced Security" API.
                builder.RegisterInstance<IFirewall>(FirewallWAS.Instance).As<IFirewall>();

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
    