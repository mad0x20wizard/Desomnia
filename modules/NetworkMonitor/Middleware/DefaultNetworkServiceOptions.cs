using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Hosts;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class DefaultNetworkServiceOptions(NetworkMonitorConfig config) : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            if (context.FirstParameterOfType<WatchedHostInfo>() is WatchedHostInfo config)
            {
                ApplyDefaultActions(config);
            }

            if (context.FirstParameterOfType<RemoteHostInfo>() is RemoteHostInfo configRemote)
            {
                ApplyDefaultKnockOptions(configRemote);
            }

            next(context);
        }

        private void ApplyDefaultActions(WatchedHostInfo config)
        {
            foreach (var service in config.Services)
            {
                service.OnDemand ??= config.OnServiceDemand;
            }
        }

        private void ApplyDefaultKnockOptions(RemoteHostInfo configHost)
        {
            foreach (var service in configHost.Services)
            {
                service.KnockMethod     ??= configHost.KnockMethod      ?? config.KnockMethod;
                service.KnockProtocol   ??= configHost.KnockProtocol    ?? config.KnockProtocol;
                service.KnockPort       ??= configHost.KnockPort        ?? config.KnockPort;

                service.KnockDelay      ??= configHost.KnockDelay       ?? config.KnockDelay;
                service.KnockRepeat     ??= configHost.KnockRepeat      ?? config.KnockRepeat;
                service.KnockTimeout    ??= configHost.KnockTimeout     ?? config.KnockTimeout;

                service.KnockSecret     ??= configHost.KnockSecret      ?? config.KnockSecret; 
                service.KnockSecretAuth ??= configHost.KnockSecretAuth  ?? config.KnockSecretAuth;
                service.KnockEncoding   ??= configHost.KnockEncoding    ?? config.KnockEncoding;
            }

        }
    }
}
