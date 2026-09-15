using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class DefaultNetworkServiceOptions(NetworkMonitorConfig config) : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            if (context.FirstParameterOfType<WatchedServiceInfo>() is WatchedServiceInfo serviceInfo)
            {
                if (context.ResolveOptional<LocalHostInfo>() is LocalHostInfo localHostInfo)
                {
                    ApplyDefaultAdvertiseOptions(serviceInfo, localHostInfo.MakeAdvertiseOptions(config));
                }

                if (context.ResolveOptional<WatchedHostInfo>() is WatchedHostInfo watchedHostInfo)
                {
                    ApplyDefaultAdvertiseOptions(serviceInfo, watchedHostInfo.MakeAdvertiseOptions(config));

                    ApplyDefaultActions(serviceInfo, watchedHostInfo);
                }

                if (context.ResolveOptional<RemoteHostInfo>() is RemoteHostInfo remoteHostInfo)
                {
                    ApplyDefaultKnockOptions(serviceInfo, remoteHostInfo);
                }
            }

            next(context);
        }

        private static void ApplyDefaultActions(WatchedServiceInfo serviceInfo, WatchedHostInfo config)
        {
            serviceInfo.OnUsage ??= config.OnServiceUsage;
            serviceInfo.OnDemand ??= config.OnServiceDemand;
        }

        private static void ApplyDefaultAdvertiseOptions(WatchedServiceInfo serviceInfo, AdvertiseOptions options)
        {
            serviceInfo.Advertise ??= options.Type;
            serviceInfo.AdvertiseTimeout ??= options.Timeout;
            serviceInfo.AdvertiseHostTTL ??= options.HostTTL;
            serviceInfo.AdvertiseServiceTTL ??= options.ServiceTTL;

            serviceInfo.Advertise &= ~AdvertiseType.Host; // remove host flag
        }

        private void ApplyDefaultKnockOptions(WatchedServiceInfo serviceInfo, RemoteHostInfo configHost)
        {
            serviceInfo.KnockMethod             ??= configHost.KnockMethod          ?? config.KnockMethod;
            serviceInfo.KnockProtocol           ??= configHost.KnockProtocol        ?? config.KnockProtocol;
            serviceInfo.KnockPort               ??= configHost.KnockPort            ?? config.KnockPort;
            serviceInfo.KnockDelay              ??= configHost.KnockDelay           ?? config.KnockDelay;
            serviceInfo.KnockRepeat             ??= configHost.KnockRepeat          ?? config.KnockRepeat;

            serviceInfo.KnockTimeout            ??= configHost.KnockTimeout         ?? config.KnockTimeout;

            serviceInfo.KnockSecret             ??= configHost.KnockSecret          ?? config.KnockSecret;
            serviceInfo.KnockSecretAuth         ??= configHost.KnockSecretAuth      ?? config.KnockSecretAuth;
            serviceInfo.KnockSecretAuthType     ??= configHost.KnockSecretAuthType  ?? config.KnockSecretAuthType;
            serviceInfo.KnockSecretEncoding     ??= configHost.KnockSecretEncoding  ?? config.KnockSecretEncoding;
        }
    }
}
