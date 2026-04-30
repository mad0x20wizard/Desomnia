using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoPollingManager(DuoStreamMonitorConfig config) : DuoManager(config)
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                bool serviceNotFound = false;
                var status = ServiceControllerStatus.Stopped;
                while (config != null && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        Service.Refresh();

                        switch (Service.Status)
                        {
                            case ServiceControllerStatus.Running when status == ServiceControllerStatus.Stopped:
                                await TriggerStarted();
                                Logger.LogDebug("Polling instances every {refresh}", config.PollInterval);
                                break;

                            case ServiceControllerStatus.Running:
                                await TriggerRefresh();
                                break;

                            case ServiceControllerStatus.Stopped when status == ServiceControllerStatus.Running:
                                TriggerStopped();
                                break;

                            // ignore these
                            case ServiceControllerStatus.StartPending:
                            case ServiceControllerStatus.StopPending:
                                continue;
                        }

                        status = Service.Status;

                        serviceNotFound = false;
                    }
                    catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception win && win.NativeErrorCode == 1060)
                    {
                        if (!serviceNotFound) // log only once
                        {
                            Logger.LogWarning(ex, "Duo service not found.");

                            serviceNotFound = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error checking instances.");
                    }

                    await Task.Delay(config.PollInterval, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // we need no more status updates
            }
        }
    }
}
