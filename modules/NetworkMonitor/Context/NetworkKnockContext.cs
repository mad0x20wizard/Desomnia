using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Events;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Services.Knocking;
using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Network.Context
{
    internal class NetworkKnockContext : Context
    {
        readonly NetworkSegment _targetNetwork;
        readonly NetworkHostRange _targetRange;

        public IList<KnockStanza> Stanzas { get; private init; } = [];

        public NetworkKnockContext(ILifetimeScope parent, NetworkMonitorConfig network, DynamicHostRangeInfo config)
        {
            string knockMethod = config.KnockMethod ?? network.KnockMethod;
            IPProtocol knockProtocol = config.KnockProtocol ?? network.KnockProtocol;
            ushort knockPort = config.KnockPort ?? network.KnockPort;
            TimeSpan knockTimeout = config.KnockTimeout ?? network.KnockTimeout;

            _targetNetwork = parent.Resolve<NetworkSegment>();
            _targetRange = parent.ResolveNamed<NetworkHostRange>(config.Name!);

            foreach (var secret in config.SharedSecret)
            {
                var scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkKnockLifetimeScopeTag, builder =>
                {
                    builder.RegisterType<KnockStanza>()
                        .WithParameter(TypedParameter.From($"{config.Name}{(secret.Label != null ? $"::{secret.Label}" : "")}")) // maybe mit index?
                        .WithParameter(TypedNamedResolvedParameter<IKnockDetector>.FindBy(knockMethod))
                        .WithParameter(TypedParameter.From(BuildSharedSecret(secret)))
                        .WithParameter(TypedParameter.From(new IPPort(knockProtocol, knockPort)))
                        .WithParameter(TypedParameter.From(knockTimeout))
                        .SingleInstance()
                        .AsSelf();

                    RegisterPacketFilter(builder, secret);
                    RegisterKnockFilter(builder, config, secret);
                });

                Stanzas.Add(ConfigureStanza(scope));

                parent.Disposer.AddInstanceForDisposal(scope); // automatic child scope disposal
            }

            parent.UseTrafficType(knockProtocol switch
            {
                IPProtocol.TCP => new TCPTrafficType(knockPort),
                IPProtocol.UDP => new UDPTrafficType(knockPort),

                _ => throw new NotImplementedException("Unknown knockProtocol: " + knockProtocol),
            });
        }

        private void RegisterPacketFilter(ContainerBuilder builder, SharedSecretData data)
        {
            // TODO implement knock packet filters
        }

        private void RegisterKnockFilter(ContainerBuilder builder, DynamicHostRangeInfo config, SharedSecretData data)
        {
            // TODO implement knock filter
        }

        private KnockStanza ConfigureStanza(ILifetimeScope scope)
        {
            var stanza = scope.Resolve<KnockStanza>();

            stanza.Knocked += KnockStanza_Knocked;

            return stanza;
        }

        private async void KnockStanza_Knocked(object? sender, KnockEventArgs args)
        {
            var stanza = (KnockStanza)sender!;

            var ip = args.Knock.SourceAddress;

            var range = new IPAddressRange(ip);

            if (_targetRange.AddAddressRange(range))
            {
                _targetNetwork.RememberHostName(ip, stanza.Label, args.Timeout);

                await Task.Delay(args.Timeout); // TODO really naive implementation

                _targetRange.RemoveAddressRange(range);
            }
        }

        private SharedSecret BuildSharedSecret(SharedSecretData data)
        {
            byte[]? key = null;
            byte[]? authKey = null;

            string defaultEncoding = data.Encoding ?? "UTF-8";

            if (data.Key is KeyData keyData)
            {
                key = SharedSecret.TryConvert(keyData.Text, keyData.Encoding ?? defaultEncoding);

                if (data.AuthKey is KeyData authKeyData)
                {
                    authKey = SharedSecret.TryConvert(authKeyData.Text, authKeyData.Encoding ?? defaultEncoding);
                }
            }
            else
            {
                key = SharedSecret.TryConvert(data.Text, defaultEncoding);
            }

            return new SharedSecret(key ?? throw new Exception($"Invalid SecretKey = '{data.Label}'"), authKey);
        }
    }
}
