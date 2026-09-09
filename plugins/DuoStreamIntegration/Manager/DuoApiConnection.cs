using Refit;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal sealed class DuoApiConnection(IDuoWebManager api, IDisposable? lifetime = null) : IDisposable
    {
        private IDisposable? _lifetime = lifetime;

        public IDuoWebManager API { get; } = api;

        public void Dispose() => Interlocked.Exchange(ref _lifetime, null)?.Dispose();
    }

    internal interface IDuoWebManager
    {
        [Get("/instances/{name}")]
        Task<bool> QueryInstance(string name, CancellationToken cancellationToken);

        [Get("/instances/{name}/start")]
        Task StartInstance(string name, CancellationToken cancellationToken);

        [Get("/instances/{name}/stop")]
        Task StopInstance(string name, CancellationToken cancellationToken);
    }
}
