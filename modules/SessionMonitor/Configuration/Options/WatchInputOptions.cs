namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct WatchInputOptions
    {
        public bool Remote { get; init; }
        public bool Disconnected { get; init; }

        public TimeSpan? MaxLastInputTime { get; init; }

        public static WatchInputOptions? operator &(WatchInputOptions? left, WatchInputOptions? right)
        {
            if (left != null && right != null) return new()
            {
                Remote              = right.Value.Remote            || left.Value.Remote,
                Disconnected        = right.Value.Disconnected      || left.Value.Disconnected,

                MaxLastInputTime    = right.Value.MaxLastInputTime  ?? left.Value.MaxLastInputTime
            };

            return null;
        }
    }
}
