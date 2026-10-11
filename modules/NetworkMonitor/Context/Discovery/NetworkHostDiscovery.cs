using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Configuration.Hosts;
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

            foreach (var configHost in Config.RemoteHost)
            {
                CreateRemoteHost(configHost);
            }

            foreach (var configHost in Config.Hosts)
            {
                CreateHost(configHost);
            }
        }

        private void CreateLocalHost()
        {
            LocalHostInfo configHost;
            if (Config.LocalHost is not null)
            {
                if (Config.Services.Any() || Config.VirtualHost.Any())
                    throw new Exception("You have to specify the configuration of local services and virtual hosts on the LocalHost node.");

                configHost = Config.LocalHost;
            }
            else
            {
                configHost = Config.WithoutNetworkFilters(); // don't register those filters twice
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

        private void CreateHost(NetworkHostInfo configHost)
        {
            if (configHost.IsNameOnly) // special handling for potential <Host> references
            {
                /**
                 * We allow to use the <Host> config nodes to reference 
                 * watched hosts (local / remote) for grouping
                 * into host ranges.
                 */
                if (configHost.IsNameOnly && _hostContexts.Any(ctx => ctx.Host.Name == configHost.Name))
                    return;
            }

            CreateHost(new TypedParameter(typeof(NetworkHostInfo), configHost));
        }

        public async Task DiscoverDynamicHosts(params FilterContext[] contexts)
        {
            foreach (var host in CreateDynamicFilterHosts(contexts))
            {
                await host.DiscoverAddresses();
            }
        }

        internal List<NetworkHostContext> CreateDynamicFilterHosts(params FilterContext[] contexts)
        {
            static IEnumerable<FilterContext> WithChildContexts(IEnumerable<FilterContext> contexts, bool onlyChildren = false)
            {
                foreach (var context in contexts)
                {
                    if (!onlyChildren)
                    {
                        yield return context;
                    }

                    if (context is IEnumerable<FilterContext> parent)
                    {
                        foreach (var ctx in WithChildContexts(parent, true))
                        {
                            yield return ctx;
                        }
                    }
                }
            }

            if (contexts.Length == 0)
            {
                contexts = [this, .. _knockContexts];
            }

            List<NetworkHostContext> created = [];
            foreach (var ctx in WithChildContexts(contexts))
            {
                var miss = ctx.FindMissingDynamicHosts(_hostContexts.Select(x => x.Host)).ToArray();

                foreach (var host in miss)
                {
                    var config = new NetworkHostInfo()
                    {
                        AutoDetect = Config.AutoDetect,

                        Name = host
                    };

                    created.Add(CreateDynamicHost(new TypedParameter(typeof(NetworkHostInfo), config)));
                }

                ctx.ResolveFilterRules(); // the rules should now be resolvable
            }

            return created;
        }

        #region Host context creation
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
        #endregion
    }
}
