using Autofac;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using System.ServiceProcess;
using WindowsFirewallHelper;

namespace MadWizard.Desomnia.Service.Duo
{
    public class PluginModule : Desomnia.ConfigurableModule<DuoConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if (Config.DuoStreamMonitor is DuoStreamMonitorConfig configMonitor)
            {
                if (!configMonitor.UsePolling)
                {
                    try
                    {
                        using var service = new ServiceController(configMonitor.ServiceName);

                        if (service.GetVersion() >= DuoEventManager.MinVersion)
                        {
                            builder.RegisterType<DuoEventManager>().As<DuoManager>()
                                .WithParameter(TypedParameter.From(configMonitor))
                                .AsImplementedInterfaces()
                                .SingleInstance();

                            goto skipPolling;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Duo Service is not available
                    }
                }

                builder.RegisterType<DuoPollingManager>().As<DuoManager>()
                    .WithParameter(TypedParameter.From(configMonitor))
                    .AsImplementedInterfaces()
                    .SingleInstance();

            skipPolling:

                // Since you're on a Windows Service (Vista+), use FirewallWAS directly
                // for the most control. It targets the "Windows Firewall with Advanced Security" API.
                builder.RegisterInstance<IFirewall>(FirewallWAS.Instance).As<IFirewall>();

                builder.RegisterType<DuoStreamMonitor>()
                    .WithParameter(TypedParameter.From(configMonitor))
                    .AsImplementedInterfaces().AsSelf()
                    .SingleInstance();

                builder.RegisterType<SunshineServiceWatch>()
                    .AsSelf();
                builder.RegisterType<SunshineServiceWatchFallback>()
                    .AsSelf();
            }
        }
    }
}
