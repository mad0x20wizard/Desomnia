using Autofac;
using MadWizard.Desomnia.Process.Manager;
using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoEventManager(DuoStreamMonitorConfig config) : DuoManager(config), IStartable, IDisposable
    {
        internal static readonly Version MinVersion = new(1, 5, 7);

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
            Service.Refresh();

            if (Service.Status == ServiceControllerStatus.Running && Service.GetPID() is uint pid)
            {
                await TriggerStarted(pid);
            }
        }

        protected async Task TriggerStarted(uint servicePID)
        {
            ProcessManager[(int)servicePID].Stopped += (sender, @event) => TriggerStopped();

            await base.TriggerStarted(servicePID);
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
                            case DuoEventID.ServiceStarted when Service.GetPID() is uint pid:
                                await TriggerStarted(pid);
                                break;

                            case DuoEventID.InstanceStarted:
                            case DuoEventID.InstanceStopped:
                            case DuoEventID.InstanceError:
                            case DuoEventID.ProcessStarted:
                            case DuoEventID.ProcessError:
                            case DuoEventID.Resuming:
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
