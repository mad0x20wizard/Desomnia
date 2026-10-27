using Autofac;
using Autofac.Builder;
using MadWizard.Desomnia.Network.Device.Filter;
using System.Net;

namespace MadWizard.Desomnia.Network
{
    internal static class ScopeExt
    {
        public static TrafficFilterRequest UseTrafficType(this ILifetimeScope scope, params ITrafficType[] types)
        {
            return scope.Resolve<TrafficFilterRequest>(new TypedParameter(typeof(ITrafficType[]), types));
        }

        public static TrafficFilterRequest UseTrafficType(this ILifetimeScope scope, IPService port)
        {
            return UseTrafficType(scope, port.Protocol switch
            {
                IPProtocol.TCP => new TCPTrafficType(port),
                IPProtocol.UDP => new UDPTrafficType(port),

                _ => throw new NotImplementedException("Unknown IP protocol: " + port.Protocol),
            });
        }
    }

    public static class MatchingScopeLifetimeTags
    {
        public static readonly object NetworkLifetimeScopeTag = "Network";
        public static readonly object NetworkHostLifetimeScopeTag = "NetworkHost";
        public static readonly object NetworkServiceLifetimeScopeTag = "NetworkService";
        public static readonly object KnockLifetimeScopeTag = "NetworkKnock";
        public static readonly object RequestLifetimeScopeTag = Autofac.Core.Lifetime.MatchingScopeLifetimeTags.RequestLifetimeScopeTag;

        public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> InstancePerNetwork<TLimit, TActivatorData, TStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TStyle> registration, params object[] lifetimeScopeTags)
        {
            ArgumentNullException.ThrowIfNull(registration, nameof(registration));

            var tags = new[] { NetworkLifetimeScopeTag }.Concat(lifetimeScopeTags).ToArray();

            return registration.InstancePerMatchingLifetimeScope(tags);
        }

        public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> InstancePerNetworkHost<TLimit, TActivatorData, TStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TStyle> registration, params object[] lifetimeScopeTags)
        {
            ArgumentNullException.ThrowIfNull(registration, nameof(registration));

            var tags = new[] { NetworkHostLifetimeScopeTag }.Concat(lifetimeScopeTags).ToArray();

            return registration.InstancePerMatchingLifetimeScope(tags);
        }

        public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> InstancePerKnockStanza<TLimit, TActivatorData, TStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TStyle> registration, params object[] lifetimeScopeTags)
        {
            ArgumentNullException.ThrowIfNull(registration, nameof(registration));

            var tags = new[] { KnockLifetimeScopeTag }.Concat(lifetimeScopeTags).ToArray();

            return registration.InstancePerMatchingLifetimeScope(tags);
        }
    }
}
