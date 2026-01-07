using Microsoft.Extensions.Logging;
using NLog;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Service.Duo.Sunshine
{
    internal class SunshineServiceWatchFallback(ILogger<SunshineServiceWatchFallback> logger, ushort port) : SunshineServiceWatch(port)
    {
        CancellationTokenSource? _cancelSource;

        public async void WaitForClient()
        {
            using TcpListener listener = new(IPAddress.Any, Service.HTTP.Port);

            try
            {
                listener.Start();

                logger.LogTrace($"Listening for incoming connections on port {Service.HTTP.Port}...");

                _cancelSource = new();

                while (_cancelSource != null)

                    try
                    {
                        using TcpClient client = await listener.AcceptTcpClientAsync(_cancelSource.Token);

                        if (client.Client.RemoteEndPoint is IPEndPoint remote)
                        {
                            if (IPAddress.IsLoopback(remote.Address))
                            {
                                logger.LogTrace("Ignored local connection attempt from {endpoint}", remote);

                                client.Close();

                                continue; // ignore local connections
                            }

                            logger.LogDebug("Accepted incoming connection attempt from {endpoint}", remote);

                            TriggerDemand();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                logger.LogTrace("Stopped to listen for incoming connections.");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                logger.LogWarning("Could not listen for incoming connections on port {port}: Address already in use.", Service.HTTP.Port);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error");
            }
        }

        public void StopWaiting()
        {
            _cancelSource?.Cancel();
            _cancelSource = null;
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            // TODO check tcp connection attempts

            if (Service.IsWaitingForClient)
            {
                if (Service.HasClientConnected)
                    ReportNetworkTraffic();
            }
            else
            {
                WaitForClient(); // long term safety net
            }

            return base.InspectResource(interval);
        }

        public override void Dispose()
        {
            base.Dispose();

            StopWaiting();
        }

    }
}
