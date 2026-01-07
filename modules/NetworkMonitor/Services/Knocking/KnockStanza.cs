using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Events;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using System.Net;

namespace MadWizard.Desomnia.Network.Services.Knocking
{
    public class KnockStanza
    {
        public required string              Label           { get; init; }

        // Detector
        public required IKnockDetector      Detector        { internal get; init; }
        // Filters
        public required IPacketFilter       PacketFilter    { internal get; init; }
        public required IKnockFilter        KnockFilter     { internal get; init; }

        // Method agnostic parameters
        public required IPPort              Port            { get; init; }
        public required TimeSpan            Timeout         { internal get; init; }
        public required SharedSecret        Secret          { internal get; init; }

        public event EventHandler<KnockEventArgs>? Knocked;

        internal void TriggerKnockEvent(KnockEvent knock)
        {
            Knocked?.Invoke(this, new()
            {
                Knock   = knock,
                Timeout = Timeout,
            });
        }
    }
}
