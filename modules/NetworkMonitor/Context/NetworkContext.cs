using Autofac;
using Autofac.Features.Metadata;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Context.Watch;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext : FilterContext, IIEnumerable<NetworkHostContext>
    {
        public required ILogger<NetworkContext> Logger { private get; init; }

        public required IVirtualMachineManager VMManager { private get; init; }

        public NetworkMonitorConfig Config { get; init; }

        public IEnumerable<NetworkPluginModule> Plugins { get; private init; }

        public NetworkDevice    Device      { get => field ??= Scope.Resolve<NetworkDevice>();  }
        public NetworkSegment   Network     { get => field ??= Scope.Resolve<NetworkSegment>(); }
        public NetworkMonitor   Monitor     { get => field ??= Scope.Resolve<NetworkMonitor>(); }

        public NetworkInterface Interface
        {
            get => Device.Interface;

            set
            {
                Device.Interface = value;

                if (IsSuspended)
                {
                    IsSuspended = false;

                    Monitor.ResumeMonitoring();
                }
            }
        }

        internal bool IsSuspended
        {
            get => field;

            set
            {
                if (field != value)
                {
                    if (field = value)
                    {
                        Monitor.SuspendMonitoring();
                    }
                }
            }
        }

        readonly IList<NetworkHostContext> _hostContexts = []; 
        readonly IList<NetworkKnockContext> _knockContexts = [];

        public NetworkContext(ILifetimeScope parent, NetworkMonitorConfig config, NetworkInterface @interface) : base(parent)
        {
            Config = config;

            Plugins = parent.Resolve<IEnumerable<Meta<NetworkPluginModule>>>() // TODO: add additional parameters
                .Where(x => (string?)x.Metadata["name"] == config.Name)
                .Select(x => x.Value);

            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag, builder =>
            {
                // Child Contexts
                builder.RegisterType<NetworkHostContext>()
                    .WithParameter(TypedParameter.From(config))
                    .ConfigurePipeline(p => p.Use(new DefaultNetworkServiceOptions(config)))
                    .InstancePerDependency()
                    .AsSelf();
                builder.RegisterType<NetworkKnockContext>()
                    .WithParameter(TypedParameter.From(config))
                    .InstancePerDependency()
                    .AsSelf();

                builder.RegisterType<NetworkDevice>()
                    .OnPreparing(e => e.Parameters = [TypedParameter.From(@interface)])
                    .ConfigurePipeline(p => p.Use(new DefaultDeviceSelector()))
                    .SingleInstance()
                    .AsSelf();
                builder.RegisterType<NetworkSegment>()
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<NetworkJanitor>()
                    .WithParameter(TypedParameter.From(config.MakeSweepOptions()))
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<NetworkMonitor>()
                    .WithParameter(TypedParameter.From(config.Name))
                    .WithParameter(TypedParameter.From(config.MakeWatchOptions()))
                    .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
                    .OnActivated(args =>
                    {
                        args.Instance.AddEventAction(nameof(NetworkMonitor.Connected), config.OnConnect);
                        args.Instance.AddEventAction(nameof(NetworkMonitor.Disconnected), config.OnDisconnect);
                    })
                    .SingleInstance()
                    .AsSelf();

                if (config.UseBPF)
                {
                    builder.RegisterType<BerkeleyPacketFilter>()
                        .AsImplementedInterfaces()
                        .InstancePerNetwork()
                        .AsSelf();
                }

                if (config.DeviceTimeout is TimeSpan timeout)
                {
                    builder.RegisterType<CaptureWatchDog>().AutoActivate()
                        .WithParameter(TypedParameter.From(timeout))
                        .AsImplementedInterfaces()
                        .InstancePerNetwork()
                        .AsSelf();
                }

                RegisterDiscovery(builder, config);

                RegisterFilters(builder, config);
                RegisterTrafficFilters(builder, config);
                RegisterHostRanges(builder, config);

                foreach (var plugin in Plugins) builder.RegisterModule(plugin);
            });

            Scope.Resolve<KnockService>(TypedParameter.From(_knockContexts.SelectMany(ctx => ctx.Stanzas)));

            parent.Disposer.AddInstanceForDisposal(Scope); // automatic child scope disposal
        }

        private void RegisterFilters(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            builder.RegisterType<StaticHostFilterRule>().AsSelf();
            builder.RegisterType<DynamicHostFilterRule>().AsSelf();
            builder.RegisterType<StaticHostRangeFilterRule>().AsSelf();
            builder.RegisterType<DynamicHostRangeFilterRule>().AsSelf();

            RegisterHostFilters(builder, config.HostFilterRule);
            RegisterHostRangeFilters(builder, config.HostRangeFilterRule);
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

        private void RegisterHostRanges(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            builder.RegisterType<LocalNetworkRange>()
                .SingleInstance()
                .AsSelf();

            foreach (var range in config.Ranges)
            {
                builder.RegisterType<NetworkHostRange>()
                    .Named<NetworkHostRange>(range.Name)
                    .SingleInstance()
                    .AsSelf();
            }
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
    }
}
