using Autofac;
using Autofac.Features.Metadata;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Context.Watch;
using MadWizard.Desomnia.Network.Datagram;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Logging;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Naming;
using MadWizard.Desomnia.Network.Naming.Resolver;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.SleepProxy;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Trace;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext : FilterContext, IIEnumerable<NetworkHostContext>
    {
        public required IVirtualMachineManager VMManager { private get; init; }

        public string Name { get; private init; }

        public NetworkMonitorConfig Config { get; init; }

        public IEnumerable<PluginModule> Plugins { get; private init; }

        public NetworkDevice    Device      { get => field ??= Scope.Resolve<NetworkDevice>();  }
        public NetworkSegment   Network     { get => field ??= Scope.Resolve<NetworkSegment>(); }
        public NetworkMonitor   Monitor     { get => field ??= Scope.Resolve<NetworkMonitor>(); }

        public INetworkInterface Interface => Device.Interface;

        internal bool IsSuspended { get; private set; }

        internal void Suspend()
        {
            if (!IsSuspended)
            {
                IsSuspended = true;

                Monitor.SuspendMonitoring();
            }
        }

        /// <summary>Resumes a suspended context on its still-present interface.</summary>
        internal void Resume()
        {
            if (IsSuspended)
            {
                IsSuspended = false;

                Monitor.ResumeMonitoring();
            }
        }

        /// <summary>
        /// Lifts the suspension WITHOUT restarting capture — for a context whose interface
        /// did not survive the sleep: the following configuration pass shuts it down, and
        /// resuming capture on the dead device first would only trip the restart-on-error loop.
        /// </summary>
        internal void EndSuspension() => IsSuspended = false;

        private readonly IList<NetworkHostContext> _hostContexts = [];
        private readonly IList<NetworkKnockContext> _knockContexts = [];

        public NetworkContext(ILifetimeScope parent, NetworkMonitorConfig config, INetworkInterface @interface) : base(parent)
        {
            Config = config;

            Name = Config.Name ?? @interface.Name;

            Plugins = parent.Resolve<IEnumerable<Meta<PluginModule, PluginModule.Metadata>>>()
                .Where(x => x.Metadata.Network is not int ordinal || ordinal == config.Ordinal)
                .Select(x => x.Value);

            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag, builder =>
            {
                builder.RegisterInstance(this); // dirty little hack, to make the Network available during the construction of the child scope

                RegisterContextAwareLogger(parent, builder);

                builder.RegisterType<NetworkMonitor>()
                    .WithParameter(TypedParameter.From(Name))
                    .WithParameter(TypedParameter.From(config.MakeWatchOptions()))
                    .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
                    .OnActivated(args =>
                    {
                        ((IEventSystem)args.Instance)[nameof(NetworkMonitor.Idle)].AddAction(config.OnIdle);
                        ((IEventSystem)args.Instance)[nameof(NetworkMonitor.Demand)].AddAction(config.OnDemand);
                        ((IEventSystem)args.Instance)[nameof(NetworkMonitor.Connected)].AddAction(config.OnConnect);
                        ((IEventSystem)args.Instance)[nameof(NetworkMonitor.Disconnected)].AddAction(config.OnDisconnect);
                    })
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<NetworkDevice>()
                    .OnPreparing(e => e.Parameters = [TypedParameter.From(@interface)])
                    .ConfigurePipeline(p => p.Use(new DefaultDeviceSelector()))
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<NetworkSegment>()
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<DroppedPacketReporter>().AutoActivate()
                    .WithParameter(TypedParameter.From(TimeSpan.FromSeconds(5)))
                    .SingleInstance()
                    .AsSelf();

                // Child Contexts
                builder.RegisterType<NetworkHostContext>()
                    .WithParameter(TypedParameter.From(config))
                    .InstancePerDependency()
                    .ExternallyOwned()
                    .AsSelf();
                builder.RegisterType<NetworkRouterContext>()
                    .WithParameter(TypedParameter.From(config))
                    .InstancePerDependency()
                    .ExternallyOwned()
                    .AsSelf();
                builder.RegisterType<NetworkServiceContext>()
                    .ConfigurePipeline(p => p.Use(new DefaultNetworkServiceOptions(config)))
                    .InstancePerDependency()
                    .ExternallyOwned()
                    .AsSelf();
                builder.RegisterType<NetworkKnockContext>()
                    .WithParameter(TypedParameter.From(config))
                    .InstancePerDependency()
                    .ExternallyOwned()
                    .AsSelf();

                builder.RegisterType<NetworkJanitor>()
                    .WithParameter(TypedParameter.From(config.MakeSweepOptions()))
                    .SingleInstance()
                    .AsSelf();

                if (config.UseBPF)
                {
                    builder.RegisterType<BerkeleyPacketFilter>()
                        .WithPriority(1)
                        .AsImplementedInterfaces()
                        .InstancePerNetwork()
                        .AsSelf();
                }

                if (config.WatchTimeout is TimeSpan timeout)
                {
                    var reg = builder.RegisterType<CaptureWatchDog>().AutoActivate()
                        .WithParameter(TypedParameter.From(timeout))
                        .AsImplementedInterfaces()
                        .InstancePerNetwork()
                        .AsSelf();

                    reg.OnActivated(x => x.Instance.GatewayTimeout = config.PingTimeout);
                }

                if (config.Where(h => h.Trace) is var tracedHosts && tracedHosts.Any())
                {
                    string[] hosts = [.. tracedHosts.Select(h => h.Name)];

                    builder.RegisterType<TraceService>()
                        .WithParameter(TypedParameter.From(new TraceService.Options() { Hosts = hosts }))
                        .AsImplementedInterfaces()
                        .InstancePerNetwork();
                }

                var mdns = builder.RegisterType<MulticastDNSService>()
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();

                mdns.OnActivated(args =>
                {
                    args.Instance.ForceUnicast = config.AdvertiseUnicast;
                });

                builder.RegisterType<HostnameResolver>()
                    .AsImplementedInterfaces()
                    .SingleInstance();
                builder.RegisterType<ServiceResolver>()
                    .AsImplementedInterfaces()
                    .SingleInstance();

                builder.RegisterType<MulticastServiceBrowser>()
                    .AsImplementedInterfaces() // IMulticastDNSListener
                    .SingleInstance()
                    .AsSelf();

                RegisterTrafficFilter(builder, new UDPTrafficType(MulticastDNSService.MulticastPort));

                builder.RegisterType<SleepProxyRegistration>()
                    .ConfigurePipeline(p => p.Use(new DefaultSleepProxyRegistration()))
                    .InstancePerDependency()
                    .AsSelf();

                builder.RegisterType<SleepProxyRegistrationMessageBurst>()
                    .ConfigurePipeline(p => p.Use(new MTUSleepProxyMessageSplitter()))
                    .InstancePerDependency()
                    .AsSelf();

                if (config.WatchMode == WatchMode.Promiscuous)
                {
                    builder.RegisterType<PromiscuousModeMutex>()
                        .WithParameter(TypedParameter.From(config.PingTimeout))
                        .AsImplementedInterfaces()
                        .InstancePerNetwork()
                        .AsSelf();

                    if (config.ShouldAdvertiseSleepProxy)
                    {
                        // The sleep proxy also receives its registrations through an OS socket
                        // (kernel-reassembled datagrams). Its port is reserved up-front -- the SRV
                        // record and the BPF whitelist need it -- and a configured port may coexist
                        // with other binders on the OS; the resolver is linked to the socket at
                        // construction via its registration metadata, and the socket closes with
                        // its last user.
                        bool sharedPort = Config.SleepProxyPort is not null;

                        ushort port = parent.Resolve<UDPSocketService>().Reserve(Config.SleepProxyPort, sharedPort);

                        var service = new SleepProxyService(port)
                        {
                            Metrics = Config.SleepProxyMetrics,

                            // Advertise which implementation is proxying, and its version -- so a browser can tell
                            // Desomnia apart from Apple's Bonjour Sleep Proxy (which doesn't publish these).

                            Properties =
                            {
                                ["impl"]    = ProductInfo.Name,
                                ["ver"]     = ProductInfo.Version
                            }
                        };

                        RegisterTrafficFilter(builder, new UDPTrafficType(service.Port));

                        builder.RegisterType<SleepProxyLease>()
                            .InstancePerDependency()
                            .AsSelf();

                        builder.RegisterType<SleepProxyRegistrar>()
                            .WithParameter(TypedParameter.From(config.AutoDetect))
                            .WithParameter(TypedParameter.From(config.MakeSleepProxyOptions()))
                            .SingleInstance()
                            .AsSelf();

                        builder.RegisterType<SleepProxyResolver>()
                            .WithParameter(new LocalHostParameter<NetworkHost>())
                            .WithParameter(TypedParameter.From(service))
                            .WithMetadata<DatagramService.SocketMetadata>(meta => meta
                                .For(m => m.Port, port)
                                .For(m => m.Shared, sharedPort))
                            .ConfigurePipeline(p => p.Use(new DatagramSocketLink())) // TODO: remove metadata and set this directly in DatagramSocketLink?
                            .AsImplementedInterfaces()
                            .SingleInstance();
                    }
                }

                if (config.AllowWakeOnLAN is WakeOnLANMode allow)
                {
                    var build = builder.RegisterType<WakeOnLANConfigurator>()
                        .WithParameter(TypedParameter.From(allow & ~WakeOnLANMode.Default))
                        .AsImplementedInterfaces()
                        .InstancePerNetwork();

                    build.OnActivated(x => x.Instance.ShouldReplace = !allow.HasFlag(WakeOnLANMode.Default));
                }

                RegisterRouterDiscovery(builder, config);
                RegisterAddressDiscovery(builder, config);
                RegisterServiceDiscovery(builder, config);

                RegisterFilters(builder, config);
                RegisterTrafficFilters(builder, config);
                RegisterHostRanges(builder, config);

                RegisterPlugins(builder);
            });

            Scope.Resolve<KnockService>(TypedParameter.From(_knockContexts.SelectMany(ctx => ctx.Stanzas)));
        }

        private void RegisterPlugins(ContainerBuilder builder)
        {
            foreach (var plugin in Plugins)
            {
                builder.RegisterModule(plugin);
            }
        }

        private void RegisterContextAwareLogger(ILifetimeScope parent, ContainerBuilder builder)
        {
            //builder.RegisterGeneric(typeof(NetworkLogger<>))
            //   .InstancePerDependency()
            //   .AsSelf();

            builder.RegisterGeneric((context, typeArguments, parameters) =>
            {
                var t = typeArguments [0];

                var loggerServiceType = typeof(ILogger<>).MakeGenericType(t);
                var wrapperType = typeof(NetworkLogger<>).MakeGenericType(t);

                var rootLogger = parent.Resolve(loggerServiceType); // The actual ILogger implementation is root scoped and should resolve to that.

                object instance = Activator.CreateInstance(wrapperType, context, rootLogger)!;

                return instance;
            }).As(typeof(ILogger<>)).InstancePerLifetimeScope();

            //builder.RegisterGenericDecorator(typeof(LoggerContextDecorator<>), typeof(ILogger<>));
        }

        private void RegisterFilters(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            builder.RegisterType<StaticHostFilterRule>().AsSelf();
            builder.RegisterType<DynamicHostFilterRule>().AsImplementedInterfaces().AsSelf();
            builder.RegisterType<StaticHostRangeFilterRule>().AsSelf();
            builder.RegisterType<DynamicHostRangeFilterRule>().AsSelf();

            RegisterHostFilters(builder, config.HostFilterRule);
            RegisterHostRangeFilters(builder, config.HostRangeFilterRule);
            RegisterEveryHostFilter(builder, config.EveryHostFilterRule);
            RegisterForeignHostFilter(builder, config.ForeignHostFilterRule);
            RegisterServiceFilters(builder, config.ServiceFilterRules);
            RegisterPingFilter(builder, config.PingFilterRule);
        }

        private void RegisterTrafficFilters(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            HashSet<ITrafficType> shapes =
            [
                new ARPTrafficType(),
                new NDPTrafficType(),
                new WOLTrafficType()
            ];

            if (config.WatchUDPPort is ushort port)
            {
                shapes.Add(new UDPTrafficType(port));
            }

            RegisterTrafficFilter(builder, [.. shapes]);
        }

        public bool Matches(object token)
        {
            if (Device == token)
                return true;
            if (Interface == token)
                return true;
            if (Monitor == token)
                return true;

            return false;
        }

        IEnumerator<NetworkHostContext> IEnumerable<NetworkHostContext>.GetEnumerator() => _hostContexts.GetEnumerator();

        public override void Dispose()
        {
            foreach (var ctx in _hostContexts.ToArray())
                ctx.Dispose();
            foreach (var ctx in _knockContexts.ToArray())
                ctx.Dispose();

            base.Dispose();
        }
    }
}
