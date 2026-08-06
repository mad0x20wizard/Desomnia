using Autofac;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoEventManager(DuoSessionMonitorConfig config) : DuoManager(config), IStartable, IDisposable
    {
        internal static readonly Version MinVersion = new(1, 5, 7);

        // serializes the ServicePID lifecycle transitions — the startup probe, the
        // event-log records and the process-exit callback arrive on three threads
        readonly AsyncLock LifecycleMutex = new();

        public required IProcessManager ProcessManager { private get; init; }

        static string XPath
        {
            get
            {
                var eventPaths = string.Join(" or ", Enum.GetValues<DuoEventID>().Select(id => "EventID=" + (int)id));

                return $"*[System[Provider[@Name='Duo'] and ({eventPaths})]]";
            }
        }

        EventLogWatcher Watcher { get; } = new(new EventLogQuery("Application", PathType.LogName, XPath)
        {
            TolerateQueryErrors = true,
        });

        void IStartable.Start()
        {
            Watcher.EventRecordWritten += EventLogWatcher_EventRecordWritten;
            Watcher.Enabled = true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                Service.Refresh();

                if (Service.Status == ServiceControllerStatus.Running && Service.PID is uint pid)
                {
                    await TriggerStarted(pid);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not determine initial Duo service state. Waiting for service events...");
            }
        }

        protected async Task TriggerStarted(uint servicePID)
        {
            using (await LifecycleMutex.LockAsync())
            {
                if (ServicePID == servicePID)
                    return;

                if (ServicePID is uint stalePID)
                {
                    // a stale ServiceStarted record (its PID was read before waiting on the
                    // mutex) must never tear down a healthy generation — only a PID the SCM
                    // currently reports may replace the adopted one
                    Service.Refresh();

                    if (Service.Status != ServiceControllerStatus.Running || Service.PID != servicePID)
                        return;

                    Logger.LogWarning("Another Duo service (PID = {NewPID}) started, before the current service (PID = {CurrentPID}) stopped.",
                        servicePID, stalePID);

                    TriggerStopped(); // tear down the stale generation, then adopt the new service
                }

                var process = ProcessManager[(int)servicePID];

                void stopHandler(object? sender, EventArgs args) => TriggerStopped(servicePID);

                process.Stopped += stopHandler;

                try
                {
                    if (process.HasStopped) // the exit may have raced our subscription and never fire it
                        throw new InvalidOperationException($"Duo service process (PID = {servicePID}) died during adoption.");

                    await base.TriggerStarted(servicePID);
                }
                catch
                {
                    // unsubscribe on ANY failure — a leftover handler would later be invoked
                    // SYNCHRONOUSLY by the process indexer's stopped-eviction while a
                    // re-adoption holds LifecycleMutex -> non-reentrant self-deadlock
                    process.Stopped -= stopHandler;

                    if (ServicePID != null)
                        TriggerStopped(); // roll back the partial adoption (ServicePID/API are
                                          // set before the fallible tail), so events can retry

                    throw;
                }
            }
        }

        private void TriggerStopped(uint servicePID)
        {
            using (LifecycleMutex.Lock())
            {
                if (ServicePID == servicePID) // ignore callbacks of an already replaced generation
                    TriggerStopped();
            }
        }

        private async void EventLogWatcher_EventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
        {
            if (args.EventException is null && args.EventRecord is EventRecord record)
            {
                var eventId = (DuoEventID)record.Id;

                try
                {
                    Service.Refresh();

                    if (Service.Status == ServiceControllerStatus.Running)
                    {
                        switch (eventId)
                        {
                            case DuoEventID.ServiceStarted when Service.PID is uint pid:
                                await TriggerStarted(pid);
                                break;

                            case DuoEventID.InstanceStarted:
                            case DuoEventID.InstanceStopped:
                            case DuoEventID.InstanceError:
                            case DuoEventID.ProcessStarted:
                            case DuoEventID.ProcessError:
                            case DuoEventID.Resuming:
                                if (ServicePID is null && Service.PID is uint runningPID)
                                    await TriggerStarted(runningPID); // recover from a rolled-back adoption
                                else
                                    await TriggerRefresh();
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Could not update Duo state ({eventId})");
                }
            }
        }

        public override void Dispose()
        {
            Watcher.Enabled = false;
            Watcher.EventRecordWritten -= EventLogWatcher_EventRecordWritten;
            Watcher.Dispose();

            base.Dispose();
        }
    }
}
