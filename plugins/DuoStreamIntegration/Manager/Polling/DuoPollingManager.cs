using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoPollingManager : DuoManager
    {
        private readonly DuoSessionMonitorConfig _config;

        public DuoPollingManager(DuoSessionMonitorConfig config) : base(config)
        {
            _config = config;
        }

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

                        if (Service.Status == ServiceControllerStatus.Running && Service.PID is uint processId)
                        {
                            if (ServicePID != processId || IsGenerationInvalidated)
                            {
                                if (ServicePID is uint previousProcessId && previousProcessId != processId)
                                {
                                    Logger.LogWarning(
                                        "Duo service PID changed from {previousPID} to {currentPID} between polls.",
                                        previousProcessId,
                                        processId);
                                }

                                await TriggerStopped();
                                await TriggerStarted(processId, stoppingToken);
                                Logger.LogDebug("Polling instances every {refresh}", _config.PollInterval);
                            }
                            else
                            {
                                await TriggerRefresh(stoppingToken);
                            }
                        }
                        else
                        {
                            await TriggerStopped();
                        }

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

                    await Task.Delay(_config.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal hosted-service shutdown.
            }
        }
    }
}
