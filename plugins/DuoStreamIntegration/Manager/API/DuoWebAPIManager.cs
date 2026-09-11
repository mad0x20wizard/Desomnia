using Microsoft.Extensions.Logging;
using Refit;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoWebAPIManager : IDuoManager, IDisposable
    {
        public required ILogger<DuoWebAPIManager> Logger { private get; init; }

        private IDuoWebManager API { get; init; }

        readonly HttpClient _client;

        public DuoWebAPIManager(HttpClient client)
        {
            API = RestService.For<IDuoWebManager>(_client = client);
        }

        public async Task<bool> QueryRunningState(DuoInstance instance, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                return await API.QueryInstance(instance.Name, token);
            }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out while refreshing {instance}.", ex);
            }
        }

        public async Task ChangeState(DuoInstance instance, bool running, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            Logger.LogInformation(
                "{operation} {instance}...",
                running ? "Starting" : "Stopping",
                instance.ToString());

            if (running)
            {
                await API.StartInstance(instance.Name, token);
            }
            else
            {
                await API.StopInstance(instance.Name, token);
            }
        }


        void IDisposable.Dispose()
        {
            _client.Dispose();
        }
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
