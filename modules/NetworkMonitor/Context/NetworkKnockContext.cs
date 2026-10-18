using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Events;
using MadWizard.Desomnia.Network.Knocking.Filter;
using MadWizard.Desomnia.Network.Knocking.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using MadWizard.Desomnia.Network.Services.Knocking;
using NetTools;
using System.Net;
using System.Text;

namespace MadWizard.Desomnia.Network.Context
{
    internal class NetworkKnockContext : FilterContext, IIEnumerable<KnockStanza>
    {
        readonly IList<ILifetimeScope> _scopes = [];

        public required NetworkSegment TargetNetwork { private get; init; }
        public required NetworkHostRange TargetRange { private get; init; }

        public NetworkKnockContext(ILifetimeScope parent, NetworkMonitorConfig network, DynamicHostRangeInfo config) : base(parent, "knock")
        {
            var method  = DetermineMethod(parent, network, config);
            var port    = DeterminePort(network, config);

            Scope = parent.BeginLifetimeScope();

            try
            {
                foreach (var secret in config.SharedSecret)
                {
                    var scope = Scope.BeginLifetimeScope(MatchingScopeLifetimeTags.KnockLifetimeScopeTag, builder =>
                    {
                        var stanza = builder.RegisterType<KnockStanza>()
                            .WithParameter(TypedNamedResolvedParameter<IKnockDetector>.FindBy(method))
                            .WithParameter(TypedParameter.From(DetermineTimeout(network, config)))
                            .WithParameter(TypedParameter.From(DetermineLabel(config, secret)))
                            .WithParameter(TypedParameter.From(BuildSharedSecret(secret)))
                            .WithParameter(TypedParameter.From(port))
                            .SingleInstance()
                            .AsSelf();

                        stanza.OnActivated(args => args.Instance.Knocked += KnockStanza_Knocked);
                        stanza.OnRelease(instance => instance.Knocked -= KnockStanza_Knocked);

                        RegisterTrafficFilter(builder, port);
                        RegisterPacketFilter(builder, config);
                        RegisterKnockFilter(builder, config);
                    });

                    Scope.Disposer.AddInstanceForDisposal(scope);

                    _scopes.Add(scope);
                }
            }
            catch
            {
                Scope.Dispose(); throw;
            }
        }

        #region Configuration coalescing
        private static string DetermineMethod(ILifetimeScope parent, NetworkMonitorConfig network, DynamicHostRangeInfo config)
        {
            var method = config.KnockMethod ?? network.KnockMethod;

            // Fail here, while the caller can still discard this context, instead of on first stanza resolution.
            if (!parent.IsRegisteredWithName<IKnockDetector>(method))
                throw new InvalidOperationException($"Unknown knock method '{method}'");

            return method;
        }

        private static IPService DeterminePort(NetworkMonitorConfig network, DynamicHostRangeInfo config)
        {
            return new IPService(config.KnockProtocol ?? network.KnockProtocol, config.KnockPort ?? network.KnockPort);
        }

        private static TimeSpan DetermineTimeout(NetworkMonitorConfig network, DynamicHostRangeInfo config)
        {
            return config.KnockTimeout ?? network.KnockTimeout;
        }

        private static string DetermineLabel(DynamicHostRangeInfo config, SharedSecretData secret)
        {
            return $"{config.Name}{(secret.Label != null ? $"::{secret.Label}" : "")}"; // maybe mit index?
        }

        private static SharedSecret BuildSharedSecret(SharedSecretData data)
        {
            byte[]? key = null;
            byte[]? authKey = null;

            DigestType authType = default;

            Encoding defaultEncoding = data.Encoding ?? Encoding.UTF8;

            if (data.Key is KeyData keyData)
            {
                key = SharedSecret.TryConvert(keyData.Text, keyData.Encoding ?? defaultEncoding);

                if (data.AuthKey is AuthKeyData authKeyData)
                {
                    authKey = SharedSecret.TryConvert(authKeyData.Text, authKeyData.Encoding ?? defaultEncoding);
                    authType = authKeyData.Type;
                }
            }
            else
            {
                key = SharedSecret.TryConvert(data.Text, defaultEncoding);
            }

            return new SharedSecret(key ?? throw new Exception($"Invalid SecretKey = '{data.Label}'"), authKey, authType);
        }
        #endregion

        #region Filter registration
        private void RegisterPacketFilter(ContainerBuilder builder, DynamicHostRangeInfo config)
        {
            RegisterTaggedPacketRuleFilter(builder);

            RegisterHostFilters(builder, config.HostFilterRule);
            RegisterHostRangeFilters(builder, config.HostRangeFilterRule);
        }

        private void RegisterKnockFilter(ContainerBuilder builder, DynamicHostRangeInfo config)
        {
            if (config.ProofIP)
            {
                builder.RegisterType<KnockSourceIPFilter>().As<IKnockFilter>()
                    .SingleInstance();
            }

            if (config.ProofTime is TimeSpan time)
            {
                builder.RegisterType<KnockTimeFilter>().As<IKnockFilter>()
                    .WithParameter(TypedParameter.From(time))
                    .SingleInstance();
            }


            if (config.ServiceFilterRule.Any())
            {
                builder.RegisterType<KnockRuleFilter>().As<IKnockFilter>()
                    .SingleInstance();

                foreach (var filter in config.ServiceFilterRule)
                {
                    builder.RegisterType<KnockPortFilterRule>()
                        .WithParameter(TypedParameter.From(filter.Type))
                        .WithParameter(TypedParameter.From(filter.Protocol))
                        .WithParameter(TypedParameter.From(filter.Port))
                        .As<KnockFilterRule>()
                        .SingleInstance();
                }
            }

            builder.RegisterComposite<CompositeKnockFilter, IKnockFilter>();
        }

        protected internal override void ResolveFilterRules()
        {
            // Knock rules live in the individual stanza scopes. Resolve them here as well,
            // once their hosts exist, so those hosts are protected by the filter references.

            foreach (var stanza in this)
            {
                _ = stanza.PacketFilter.Value;
            }
        }
        #endregion

        private void KnockStanza_Knocked(object? sender, KnockEventArgs args)
        {
            var stanza = (KnockStanza)sender!;

            var ip = args.SourceEndPoint.Address;

            TargetRange.AddAddressRange(new IPAddressRange(ip), new IPAddressRangeOptions(args.Timeout));

            TargetNetwork.RememberHostName(ip, stanza.Label, args.Timeout);
        }

        /// <summary>
        /// Each scope owns a single stanza; its packet filter resolves separately after
        /// the caller has created any missing dynamic filter hosts.
        /// </summary>
        IEnumerator<KnockStanza> IEnumerable<KnockStanza>.GetEnumerator()
        {
            return _scopes.Select(scope => scope.Resolve<KnockStanza>()).GetEnumerator();
        }
    }
}
