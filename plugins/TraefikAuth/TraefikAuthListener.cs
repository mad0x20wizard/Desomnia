using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace MadWizard.Desomnia.Network.Traefik
{
    internal class TraefikAuthListener : INetworkService
    {
        public required ILogger<TraefikAuthListener> Logger { private get; init; }

        public required IEnumerable<TraefikEndpoint> Endpoints { private get; init; }

        readonly HttpListener _http = new();

        void INetworkService.Startup()
        {
            foreach (var endpoint in Endpoints)
            {
                if (_http.Prefixes.Contains(endpoint.AuthPrefix))
                    _http.Prefixes.Add(endpoint.AuthPrefix); // MAYBE: allow port in firewall

                Logger.LogDebug("Listening on port 5000...");
            }

            _http.Start(); // catch (Address already in use) on Linux

            Listen();
        }

        async void Listen()
        {
            try
            {
                while (true)
                {
                    var ctx = await _http.GetContextAsync();

                    var request = ctx.Request; var response = ctx.Response;
                    if (Endpoints.FirstOrDefault(end => end.Accepts(request)) is TraefikEndpoint endpoint)
                    {
                        Logger.LogDebug("Received request: {Method} {Url} from {RemoteEndPoint}", request.HttpMethod, request.Url, request.RemoteEndPoint);

                        var hoststring = ($"[{request.RemoteEndPoint}] {request.HttpMethod} {request.Url}");

                        string headerstring;
                        foreach (string headerName in request.Headers)
                            headerstring = ($"{headerName}: {request.Headers[headerName]}");

                    }

                    var responseText = "OK\n";
                    var buffer = Encoding.UTF8.GetBytes(responseText);
                    response.StatusCode = 200;
                    response.ContentType = "text/plain";
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in Listen");
            }
        }

        void INetworkService.Shutdown()
        {
            _http.Stop();

            Logger.LogDebug("Stopped listening on port 5000...");
        }
    }
}
