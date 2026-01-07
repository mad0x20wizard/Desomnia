namespace MadWizard.Desomnia.Network.Reachability
{
    internal readonly struct ReachabilityCacheEntry(bool alive)
    {
        public DateTime Time    { get; init; } = DateTime.Now;
        public bool     Alive   { get; init; } = alive;

        public bool IsExpired(TimeSpan maxAge) => (DateTime.Now - Time) > maxAge;
    }
}
