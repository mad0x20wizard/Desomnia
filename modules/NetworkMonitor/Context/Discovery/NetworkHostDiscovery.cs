using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        internal async Task DiscoverHosts()
        {
            Logger.LogDebug("Discovering network hosts...");

            CreateLocalHost();

            foreach (var configHost in Config.Hosts)
            {
                CreateHost(new TypedParameter(typeof(NetworkHostInfo), configHost));
            }

            foreach (var configHost in Config.RemoteHost)
            {
                CreateRemoteHost(configHost);
            }
        }

        internal IEnumerable<NetworkHostContext> CreateDynamicFilterHosts()
        {
            List<NetworkHostContext> created = []; // eager on purpose: callers may discard the result

            var contexts = ((IEnumerable<FilterContext>)[this])
                .Concat(_hostContexts).Concat(_hostContexts.SelectMany(ctx => ctx))
                .Concat(_knockContexts).ToList();

            foreach (var ctx in contexts)
            {
                foreach (var host in ctx.FindMissingDynamicHosts(_hostContexts.Select(x => x.Host)).ToArray())
                {
                    var config = new NetworkHostInfo()
                    {
                        AutoDetect = Config.AutoDetect,

                        Name = host
                    };

                    created.Add(CreateDynamicHost(new TypedParameter(typeof(NetworkHostInfo), config)));
                }

                ctx.Scope?.Resolve<IEnumerable<PacketFilterRule>>(); // the rules should now be resolvable
            }

            return created;
        }

        private void CreateLocalHost()
        {
            LocalHostInfo configHost;
            if (Config.LocalHost is not null)
            {
                if (Config.Service.Any() || Config.HTTPService.Any() || Config.VirtualHost.Any())
                    throw new Exception("You have to specify the configuration of local services and virtual hosts on the LocalHost node.");

                configHost = Config.LocalHost;
            }
            else
            {
                Config.HostFilterRule.Clear(); // don't register these filters twice

                configHost = Config;
            }

            CreateHost(new TypedParameter(typeof(LocalHostInfo), configHost));

            foreach (var configHostVirtual in configHost.VirtualHost)
            {
                if (VMManager[configHostVirtual.Name!] is IVirtualMachine vm)
                {
                    CreateHost(
                        new TypedParameter(typeof(LocalVirtualHostInfo), configHostVirtual),
                        new TypedParameter(typeof(IVirtualMachine), vm)
                    );
                }
            }
        }

        private void CreateRemoteHost(RemotePhysicalHostInfo configHost)
        {
            CreateHost(new TypedParameter(typeof(RemotePhysicalHostInfo), configHost));

            foreach (var configHostVirtual in configHost.VirtualHost)
            {
                CreateHost(new TypedParameter(typeof(RemoteVirtualHostInfo), configHostVirtual), TypedParameter.From(configHost));
            }
        }

        internal NetworkHostContext CreateHost(params Parameter[] parameters)
        {
            return CreateHost<NetworkHostContext>(parameters);
        }

        internal T CreateHost<T>(params Parameter[] parameters) where T : NetworkHostContext
        {
            var context = Scope.Resolve<T>(parameters);

            Network.AddHost(context.Host);

            string type = context.Host.ToHostTypeString();

            Logger.LogDebug("Created {type} '{Name}'", type, context.Host.Name);

            if (context.Watch is NetworkHostWatch watch)
            {
                this.Monitor.StartTracking(watch);
            }

            context.Scope.CurrentScopeEnding += (sender, args) =>
            {
                Network.RemoveHost(context.Host);

                Logger.LogDebug("Removed {type} '{Name}'", type, context.Host.Name);

                if (context.Watch is NetworkHostWatch watch)
                {
                    this.Monitor.StopTracking(watch);
                }

                _hostContexts.Remove(context);
            };

            _hostContexts.Add(context);

            return context;
        }

        public NetworkHostContext CreateDynamicHost(params Parameter[] parameters)
        {
            var context = CreateHost(parameters);

            Scope.Resolve<NetworkJanitor>().MakeHostEligibleForSweeping(context);

            return context;
        }
    }
}
