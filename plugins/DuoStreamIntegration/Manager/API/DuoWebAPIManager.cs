using Microsoft.Extensions.Logging;
using Refit;
using System.Diagnostics;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoWebAPIManager : IDuoManager, IDisposable
    {
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

        public required ILogger<DuoWebAPIManager> Logger { private get; init; }

        private IDuoWebManager API { get; init; }

        readonly HttpClient _client;
        readonly RefitSettings _settings;

        public DuoWebAPIManager(HttpClient client)
        {
            _settings = new RefitSettings
            {
                TransportExceptionFactory = HandleTransportException
            };

            API = RestService.For<IDuoWebManager>(_client = client, _settings);
        }

        Exception HandleTransportException(HttpRequestMessage request, Exception exception, CancellationToken token)
        {
            if (exception is OperationCanceledException && token.IsCancellationRequested)
                return exception;
            
            return new ApiRequestException(request, request.Method, _settings, exception);
        }

        public async Task<bool> QueryState(DuoInstance instance, CancellationToken token)
        {
            using var cancellation = token.WithTimeout(QueryTimeout);

            var watch = Stopwatch.StartNew();

            var running = await API.QueryInstance(instance.Name, cancellation.Token);

            Logger.LogTrace("Queried {Instance} = {State} [{Duration} ms]",
                instance.ToString(), running ? "running" : "stopped",
                Math.Ceiling(watch.Elapsed.TotalMilliseconds));

            return running;
        }

        public async Task RequestState(DuoInstance instance, bool running, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();

            if (running)
            {
                await API.StartInstance(instance.Name, token);
            }
            else
            {
                await API.StopInstance(instance.Name, token);
            }

            Logger.LogTrace("Requested {Instance} -> {State} [{Duration} ms]",
                instance.ToString(), running ? "running" : "stopped",
                Math.Ceiling(watch.Elapsed.TotalMilliseconds));
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
