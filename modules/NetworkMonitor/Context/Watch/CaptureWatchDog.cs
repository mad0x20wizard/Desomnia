using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Context.Watch
{
    internal class CaptureWatchDog(TimeSpan timeout) : INetworkService
    {
        public required ILogger<CaptureWatchDog> Logger { private get; init; }

        public required DynamicNetworkObserver Observer { private get; init; }

        public required NetworkDevice   Device { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        public required Lazy<ReachabilityService> Reachability { private get; init; }

        CancellationTokenSource? _cancel = null;
        DateTime _lastTimeReceived = DateTime.Now;
        bool _hasTestedRouterReachability = false;
        bool _hasRestartedCapture = false;

        #region NetworkService
        void INetworkService.Resume()
        {
            _cancel = new CancellationTokenSource();

            Logger.LogDebug($"Checking device '{Device.Name}' every {timeout}");

            Watch(_cancel.Token);
        }
        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            _lastTimeReceived = DateTime.Now;
        }
        void INetworkService.Suspend()
        {
            _cancel?.Cancel();
            _cancel = null;
        }
        #endregion

        private async void Watch(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(timeout, token);

                    if ((DateTime.Now - _lastTimeReceived) > timeout)
                    {
                        if (!_hasTestedRouterReachability && Network.DefaultGateway is NetworkRouter router)
                        {
                            Logger.LogDebug($"Received NO packets on device '{Device.Name}' for {timeout}, testing reachability of default gateway:");

                            try
                            {
                                var latency = await Reachability.Value.Send(new(router.IPAddresses, timeout));

                                Logger.LogTrace($"Default gateway responded after {Math.Ceiling(latency.TotalMilliseconds)} ms");

                                continue;
                            }
                            catch (HostTimeoutException)
                            {
                                //Logger.LogTrace($"Received NO response from default gateway after {Math.Ceiling(ex.Timeout.TotalMilliseconds)} ms");
                            }

                            _hasTestedRouterReachability = true;
                        }

                        if (_hasRestartedCapture)
                        {
                            Logger.LogWarning($"Received NO packets on device '{Device.Name}' for {timeout}, reconfiguring network monitors...");

                            await Observer.ReconfigureNetworkMonitors();
                        }
                        else
                        {
                            Logger.LogWarning($"Received NO packets on device '{Device.Name}' for {timeout}, restarting capture...");

                            Device.StopCapture();
                            Device.StartCapture();

                            _hasRestartedCapture = true;
                        }
                    }
                    else
                    {
                        _hasTestedRouterReachability = false;
                        _hasRestartedCapture = false;
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"An error while checking device '{Device.Name}'");
                }
            }
        }
    }
}
