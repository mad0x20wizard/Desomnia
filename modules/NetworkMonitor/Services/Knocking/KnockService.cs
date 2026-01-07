using Autofac.Features.Indexed;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Services;
using MadWizard.Desomnia.Network.Services.Knocking;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Knocking
{
    public class KnockService : INetworkService
    {
        public required ILogger<KnockService> Logger { private get; init; }

        public required IEnumerable<KnockStanza> Stanzas { get; init; }

        public required IIndex<string, IKnockMethod> Methods { private get; init; }

        void INetworkService.Startup()
        {
            foreach (var stanza in Stanzas)
            {
                Logger.LogDebug($"Listening on {stanza.Port} for SPA stanza '{stanza.Label}'" +
                    $" using <{stanza.Detector.GetType().FullName}>");
            }
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            if (packet.PayloadPacket is IPPacket ip && ip.PayloadPacket is TransportPacket transport) // TODO: filter for target TriggerIPPacket?
            {
                foreach (var stanza in Stanzas.Where(stanza => stanza.Port.Accepts(transport)))
                {
                    if (stanza.PacketFilter.ShouldFilter(packet)) continue; // maybe filter packet

                    foreach (var knock in stanza.Detector.Examine(ip, stanza.Secret))
                    {
                        if (stanza.KnockFilter.ShouldFilter(ip, knock)) continue; // maybe filter knock

                        Logger.LogDebug($"Received valid knock from {knock.SourceAddress}" +
                            (knock.TargetPort != null ? $" to access {knock.TargetPort}" : "") +
                            $" via stanza '{stanza.Label}'");

                        stanza.TriggerKnockEvent(knock);
                    }
                }
            }
        }

        public async Task DoSinglePacketAuthorization(string methodName, IPAddress source, IPEndPoint target, IPPort knock, SharedSecret secret)
        {
            if (!Methods.TryGetValue(methodName, out var method))
                throw new InvalidOperationException($"Unknown knock method: {methodName}");

            // LATER: replace passthorugh secret
            // LATER: resolve public IP

            method.Knock(source, target, knock, secret);
        }
    }
}
