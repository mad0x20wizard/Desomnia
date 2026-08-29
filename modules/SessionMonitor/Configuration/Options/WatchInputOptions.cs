namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct WatchInputOptions
    {
        public bool Remote { get; init; }
        public bool Disconnected { get; init; }

        public static WatchInputOptions? operator +(WatchInputOptions? left, WatchInputOptions? right)
        {
            if (left != null && right != null) return new()
            {
                Remote          = left.Value.Remote         || right.Value.Remote,
                Disconnected    = left.Value.Disconnected   || right.Value.Disconnected,
            };

            return null;
        }
    }
}
