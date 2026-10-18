using Autofac.Features.Indexed;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Services.Knocking;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Knocking
{
    public class KnockService : INetworkService
    {
        public required ILogger<KnockService> Logger { private get; init; }

        public required IIndex<string, IKnockMethod> Methods { private get; init; }

        #region Stanza management
        private readonly HashSet<KnockStanza> _stanzas = [];

        internal void AddStanza(KnockStanza stanza)
        {
            if (stanza.Detector is IKnockValidation validation)
                validation.ValidateSecret(stanza.Secret);

            lock (_stanzas)
            {
                using var scope = Logger.BeginRealmScope("knocking");

                if (_stanzas.Add(stanza))
                    Logger.LogDebug($"Listening on {stanza.Port} for SPA stanza '{stanza.Label}'" +
                        $" using <{stanza.Detector.Name}>");
            }
        }

        internal void RemoveStanza(KnockStanza stanza)
        {
            lock (_stanzas)
            {
                _stanzas.Remove(stanza);
            }
        }
        #endregion

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            if (packet.PayloadPacket is IPPacket ip && ip.PayloadPacket is TransportPacket transport) // TODO: filter for target TriggerIPPacket?
            {
                lock (_stanzas) foreach (var stanza in _stanzas.Where(stanza => stanza.Port.Accepts(transport)).ToArray())
                {
                    using var scope = Logger.BeginRealmScope("knocking");

                    try
                    {
                        if (stanza.PacketFilter.Value.ShouldFilter(packet)) continue; // maybe filter packet

                        foreach (var knock in stanza.Detector.Examine(ip, stanza.Secret))
                        {
                            if (stanza.KnockFilter.ShouldFilter(ip.SourceEndPoint, knock)) continue; // maybe filter knock

                            Logger.LogDebug($"Received valid knock from {ip.SourceAddress}" +
                                (knock.TargetPort != null ? $" to access {knock.TargetPort}" : "") +
                                $" via stanza '{stanza.Label}'");

                            stanza.TriggerKnockEvent(ip.SourceEndPoint, knock);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Could not process knock stanza '{Label}' using <{Method}>", stanza.Label, stanza.Detector.Name);
                    }
                }
            }
        }

        public async Task DoSinglePacketAuthorization(string methodName, IPAddress source, IPEndPoint target, IPService knock, SharedSecret secret)
        {
            if (!Methods.TryGetValue(methodName, out var method))
                throw new InvalidOperationException($"Unknown knock method '{methodName}'");

            // LATER: replace passthorugh secret
            // LATER: resolve public IP

            method.Knock(source, target, knock, secret);
        }
    }
}
