using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Configuration.Migration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using MadWizard.Desomnia.Service.Duo.Sunshine.Listener;
using MadWizard.Desomnia.Service.Duo.Sunshine.Watch;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using System.ComponentModel;
using System.ServiceProcess;
using System.Xml.Linq;
using WindowsFirewallHelper;

namespace MadWizard.Desomnia.Service.Duo
{
    public class PluginModule : Desomnia.ConfigurableModule<DuoConfig>, IXConfigurationMigration
    {
        #region Versioning
        protected override uint MinVersion => 2;

        void IXConfigurationMigration.Run(XDocument configuration, uint version)
        {
            switch (version)
            {
                case 2: V2.Run(configuration); break;
            }
        }
        #endregion

        protected override void Load(ContainerBuilder builder, DuoConfig config)
        {
            if (config.DuoSessionMonitor is DuoSessionMonitorConfig duo)
            {
                builder.RegisterType<DuoService>().AsSelf()
                    .WithParameter(TypedParameter.From(duo.ServiceName))
                    .SingleInstance();

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

                builder.RegisterType<DuoInstance>().AsSelf()
                    .InstancePerDependency();

                builder.RegisterType<DuoServiceContext>().AsSelf()
                    .ConfigurePipeline(p => p.Use(new ConfigureContext(config.DuoSessionMonitor)))
                    .InstancePerDependency();

                // the only available DuoManager right now
                builder.RegisterType<DuoWebAPIManager>()
                    .InstancePerOwned<DuoServiceContext>()
                    .As<IDuoManager>().AsSelf();

                if (!duo.UsePolling)
                {
                    try
                    {
                        using var service = new ServiceController(duo.ServiceName);

                        if (service.Version >= EventWatcher.MinVersion)
                        {
                            builder.RegisterType<EventWatcher>().As<IDuoInstanceWatcher>()
                                .InstancePerOwned<DuoServiceContext>();

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

                builder.RegisterType<PollingWatcher>().As<IDuoInstanceWatcher>()
                    .WithParameter(TypedParameter.From(duo.PollInterval))
                    .InstancePerOwned<DuoServiceContext>();

            skipPolling:

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
                        .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(DuoSessionMonitor))))
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
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(DuoSessionMonitor))))
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
