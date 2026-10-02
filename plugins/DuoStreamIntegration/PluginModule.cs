using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Configuration.Migration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using MadWizard.Desomnia.Service.Duo.Session;
using MadWizard.Desomnia.Service.Duo.Sunshine.Listener;
using MadWizard.Desomnia.Service.Duo.Sunshine.Watch;
using MadWizard.Desomnia.Session.Configuration;
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
                if (config.SessionMonitor is not SessionMonitorConfig session)
                    throw new FormatException("DuoSessionMonitor requires that <SessionMonitor> must be enabled");

                builder.RegisterType<DuoService>().AsSelf()
                    .WithParameter(TypedParameter.From(duo.ServiceName))
                    .SingleInstance();

                // DuoManager requires an ISessionManager (only present in service mode),
                // so the whole monitor block degrades to inert without one
                var monitorDuo = builder.RegisterType<DuoSessionMonitor>()
                    .AsImplementedInterfaces().AsSelf()
                    .SingleInstance();

                monitorDuo.OnActivated(args =>
                {
                    ((IEventSystem)args.Instance)[nameof(DuoSessionMonitor.Idle)].AddAction(duo.OnIdle);
                    ((IEventSystem)args.Instance)[nameof(DuoSessionMonitor.Usage)].AddAction(duo.OnUsage);
                });

                builder.RegisterType<DuoServiceContext>().AsSelf()
                    .ConfigurePipeline(p => p.Use(new ContextConfiguration(config.DuoSessionMonitor)))
                    .InstancePerDependency();

                builder.RegisterType<DuoInstance>().AsSelf()
                    .InstancePerDependency();

                RegisterManager(builder);
                RegisterWatchers(builder, duo);

                builder.RegisterType<SessionWatchAdapter>()
                    .WithParameter(new TypedParameter(typeof(SessionMonitorConfig), session))
                    .OnActivated(ctx => ctx.Instance.Attach()).AutoActivate()
                    .AsImplementedInterfaces()
                    .SingleInstance();

                if (config.UseListener)
                {
                    SunshineListenerModule.Validate(duo);

                    builder.RegisterModule<SunshineListenerModule>();
                }
                else
                {
                    builder.RegisterType<NetworkPluginModule>()
                        .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(DuoSessionMonitor))))
                        .As<Desomnia.Network.PluginModule>()
                        .SingleInstance();
                }

                builder.RegisterBuildCallback(container => container.ResolveOptional<DuoSessionMonitor>()?.Startup());
            }
        }

        void RegisterManager(ContainerBuilder builder)
        {
            // the only available DuoManager right now
            builder.RegisterType<DuoWebAPIManager>()
                .InstancePerOwned<DuoServiceContext>()
                .As<IDuoManager>().AsSelf();
        }

        void RegisterWatchers(ContainerBuilder builder, DuoSessionMonitorConfig config)
        {
            using var service = new ServiceController(config.ServiceName);

            builder.RegisterComposite<CompositeWatcher, IDuoWatcher>()
                .InstancePerOwned<DuoServiceContext>();

            builder.RegisterType<SessionWatcher>()
                .InstancePerOwned<DuoServiceContext>()
                .As<IDuoWatcher>();

            try
            {
                /**
                 * TODO: improve auto mode and add exception if none Watcher matched, 
                 * so that we fail early here.
                 */
                foreach (var mode in config.WatchModes())
                {
                    switch (mode) 
                    {
                        case WatchMode.Registry:
                            builder.RegisterType<RegistryWatcher>()
                                .InstancePerOwned<DuoServiceContext>()
                                .As<IDuoWatcher>(); break;

                        case WatchMode.EventLog when service.Version >= PreciseEventWatcher.MinVersion:
                            builder.RegisterType<PreciseEventWatcher>()
                                .InstancePerOwned<DuoServiceContext>()
                                .As<IDuoWatcher>(); break;

                        case WatchMode.EventLog when service.Version >= EventWatcher.MinVersion:
                            builder.RegisterType<EventWatcher>()
                                .InstancePerOwned<DuoServiceContext>()
                                .As<IDuoWatcher>(); break;

                        case WatchMode.Polling when config.PollInterval is TimeSpan interval:
                            builder.RegisterType<PollingWatcher>()
                                .WithParameter(TypedParameter.From(interval))
                                .InstancePerOwned<DuoServiceContext>()
                                .As<IDuoWatcher>(); break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Registration of DuoWatcher failed", ex);
            }
        }
    }

    internal class SunshineListenerModule : Autofac.Module
    {
        internal static void Validate(DuoSessionMonitorConfig config)
        {
            foreach (var instance in config.Instance)
            {
                if (instance.MinStreamTraffic != null)
                    throw new FormatException("Cannot monitor MinStreamTraffic while in Listener Mode.");

                if (instance.HostFilterRule.Count > 0 || instance.HostRangeFilterRule.Count > 0)
                    throw new FormatException($"Duo instance '{instance.Name}': HostFilterRule and HostRangeFilterRule require packet capture and cannot be used in listener mode.");
            }
        }

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
                .OnActivated(ctx => ctx.Instance.Attach()).AutoActivate()
                .SingleInstance();
        }
    }

    internal class NetworkPluginModule : Desomnia.Network.PluginModule
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
