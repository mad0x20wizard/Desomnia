using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoPollingManager(DuoSessionMonitorConfig config) : DuoManager(config)
    {
        protected override async Task RunAsync(CancellationToken stoppingToken)
        {
            var serviceNotFound = false;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        Service.Refresh();

                        await Reconcile(Service.Status == ServiceControllerStatus.Running ? Service.PID : null, stoppingToken);

                        serviceNotFound = false;
                    }
                    catch (InvalidOperationException ex) when (
                        ex.InnerException is Win32Exception { NativeErrorCode: 1060 })
                    {
                        await TriggerStopped();

                        if (!serviceNotFound)
                        {
                            Logger.LogWarning(ex, "Duo service not found.");
                            serviceNotFound = true;
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error checking Duo instances.");
                    }

                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal hosted-service shutdown.
            }
        }

        protected override async Task Adopt(uint processId, CancellationToken stoppingToken)
        {
            await base.Adopt(processId, stoppingToken);
            Logger.LogDebug("Polling instances every {refresh}", PollInterval);
        }
    }
}
