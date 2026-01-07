namespace MadWizard.Desomnia.Network.Knocking.Events
{
    public class KnockEventArgs : EventArgs
    {
        public required KnockEvent Knock { get; init; }
        public required TimeSpan Timeout { get; init; }
    }
}
