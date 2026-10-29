using Autofac.Features.Indexed;
using MadWizard.Desomnia.Network.Capture;
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

        public required IFilterContextParser Parser { private get; init; }

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
                    Logger.LogDebug($"Listening on {stanza.Service} for SPA stanza '{stanza.Label}'" +
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

        void INetworkService.ProcessCapture(INetworkCapture capture)
        {
            if (capture.TargetEndPoint is IPServiceEndPoint target && capture.SourceEndPoint is IPEndPoint source)
            {
                lock (_stanzas) foreach (var stanza in _stanzas.Where(stanza => stanza.Service == target).ToArray())
                {
                    using var scope = Logger.BeginRealmScope("knocking");

                    try
                    {
                        if (stanza.PacketFilter.Value.ShouldFilter(Parser.CreateContext(new(ref capture)))) continue;

                        var ip = capture.FindPacket<IPPacket>() ?? throw new IOException("IPPacket not found");

                        foreach (var knock in stanza.Detector.Examine(ip, stanza.Secret))
                        {
                            if (stanza.KnockFilter.ShouldFilter(source, knock)) continue; // maybe filter knock

                            Logger.LogDebug($"Received valid knock from {source.Address}" +
                                (knock.TargetPort != null ? $" to access {knock.TargetPort}" : "") +
                                $" via stanza '{stanza.Label}'");

                            stanza.TriggerKnockEvent(source, knock);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Could not process knock stanza '{Label}' using <{Method}>", stanza.Label, stanza.Detector.Name);
                    }
                }
            }
        }

        public async Task DoSinglePacketAuthorization(string methodName, IPAddress source, IPServiceEndPoint target, IPService knock, SharedSecret secret)
        {
            if (!Methods.TryGetValue(methodName, out var method))
                throw new InvalidOperationException($"Unknown knock method '{methodName}'");

            // LATER: replace passthorugh secret
            // LATER: resolve public IP

            method.Knock(source, target, knock, secret);
        }
    }
}
