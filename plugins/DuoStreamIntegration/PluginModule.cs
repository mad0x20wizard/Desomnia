using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine.Listener;
using MadWizard.Desomnia.Service.Duo.Sunshine.Watch;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using System.ComponentModel;
using System.ServiceProcess;
using WindowsFirewallHelper;

namespace MadWizard.Desomnia.Service.Duo
{
    public class PluginModule : Desomnia.ConfigurableModule<DuoConfig>
    {
        protected override void Load(ContainerBuilder builder, DuoConfig config)
        {
            if (config.DuoSessionMonitor is DuoSessionMonitorConfig duo)
            {
                // DuoManager requires an ISessionManager (only present in service mode),
                // so the whole monitor block degrades to inert without one
                var monitorDuo = builder.RegisterType<DuoSessionMonitor>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(ISessionManager))))
                    .WithParameter(TypedParameter.From(duo))
                    .AsImplementedInterfaces().AsSelf()
                    .SingleInstance();

                monitorDuo.OnActivated(args =>
                {
                    ((IEventSystem)args.Instance)[nameof(DuoSessionMonitor.Idle)].AddAction(duo.OnIdle);
                    ((IEventSystem)args.Instance)[nameof(DuoSessionMonitor.Demand)].AddAction(duo.OnDemand);
                });

                if (!duo.UsePolling)
                {
                    try
                    {
                        using var service = new ServiceController(duo.ServiceName);

                        if (service.Version >= DuoEventManager.MinVersion)
                        {
                            builder.RegisterType<DuoEventManager>().As<DuoManager>()
                                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(ISessionManager))))
                                .WithParameter(TypedParameter.From(duo))
                                .AsImplementedInterfaces()
                                .SingleInstance();

                            goto skipPolling;
                        }
                    }
                    catch (Exception ex) when
                    (ex is Win32Exception
                        or FileNotFoundException
                        or FormatException
                        or ArgumentException   // Version.Parse on structurally odd FileVersion strings
                        or OverflowException
                        or InvalidDataException
                        or InvalidOperationException)
                    {
                        // Duo Service is not available (or its version is unreadable)
                    }
                }

                builder.RegisterType<DuoPollingManager>().As<DuoManager>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(ISessionManager))))
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

                if (config.SessionMonitor is SessionMonitorConfig monitorSession)
                    builder.RegisterType<SessionWatchAdapter>()
                        .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(SessionMonitor))))
                        .WithParameter(new TypedParameter(typeof(SessionMonitorConfig), monitorSession))
                        .AsImplementedInterfaces()
                        .SingleInstance();

                if (config.UseListener)
                {
                    builder.RegisterModule<SunshineListenerModule>();
                }
                else
                {
                    builder.RegisterType<NetworkPluginModule>()
                        .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(DuoManager))))
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
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(DuoManager))))
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
