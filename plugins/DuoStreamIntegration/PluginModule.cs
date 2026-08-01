using Autofac;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine.Listener;
using MadWizard.Desomnia.Service.Duo.Sunshine.Watch;
using System.ServiceProcess;
using WindowsFirewallHelper;

namespace MadWizard.Desomnia.Service.Duo
{
    public class PluginModule : Desomnia.ConfigurableModule<DuoConfig>
    {
        protected override void Load(ContainerBuilder builder, DuoConfig config)
        {
            if (config.DuoStreamMonitor is DuoStreamMonitorConfig duo)
            {
                var monitor = builder.RegisterType<DuoStreamMonitor>()
                    .WithParameter(TypedParameter.From(duo))
                    .AsImplementedInterfaces().AsSelf()
                    .SingleInstance();

                monitor.OnActivated(args =>
                {
                    ((IEventSystem)args.Instance)[nameof(DuoStreamMonitor.Idle)].AddAction(duo.OnIdle);
                    ((IEventSystem)args.Instance)[nameof(DuoStreamMonitor.Demand)].AddAction(duo.OnDemand);
                });

                if (!duo.UsePolling)
                {
                    try
                    {
                        using var service = new ServiceController(duo.ServiceName);

                        if (service.Version >= DuoEventManager.MinVersion)
                        {
                            builder.RegisterType<DuoEventManager>().As<DuoManager>()
                                .WithParameter(TypedParameter.From(duo))
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
                    .WithParameter(TypedParameter.From(duo))
                    .AsImplementedInterfaces()
                    .SingleInstance();

            skipPolling:

                // instances are container-created (spec §2: the root assumption that
                // every eventable object is managed by the container) — the manager
                // builds them through the auto-generated delegate factory and OWNS
                // their disposal (container tracking would pin every replaced
                // generation until application shutdown)
                builder.RegisterType<DuoInstance>().AsSelf().ExternallyOwned();

                if (config.UseFallback)
                {
                    builder.RegisterModule<SunshineListenerModule>();
                }
                else
                {
                    builder.RegisterType<NetworkPluginModule>()
                        .As<Desomnia.Network.PluginModule>()
                        .SingleInstance();
                }
            }
        }
    }

    internal class SunshineListenerModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterInstance<IFirewall>(FirewallWAS.Instance).As<IFirewall>();

            var listener = builder.RegisterType<SunshineListener>()
                .InstancePerDependency()
                .AsSelf();

            // trigger WaitForClient(), if Sunshine is not running
            listener.OnActivated(args => args.Instance.Inspect(TimeSpan.Zero));

            builder.RegisterType<SunshineListenerAdapter>()
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }

    public class NetworkPluginModule : Desomnia.Network.PluginModule
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<SunshineServiceContext>()
                .InstancePerDependency()
                .AsSelf();

            builder.RegisterType<SunshineServiceContextAdapter>()
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }
}
