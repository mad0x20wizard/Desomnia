namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal interface IDuoWatcher
    {
        IAsyncEnumerable<WatchSignal> WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token);
    }

    internal readonly struct WatchSignal(DuoInstance? instance = null, bool? running = null)
    {
        public DuoInstance? Instance { get; init; } = instance;

        public bool? IsRunning { get; init; } = running;
    }
}
