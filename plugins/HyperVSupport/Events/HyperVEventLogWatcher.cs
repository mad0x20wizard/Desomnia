using Autofac;
using MadWizard.Desomnia.Network.HyperV.Manager;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using NLog;
using System.Diagnostics.Eventing.Reader;

namespace MadWizard.Desomnia.Network.HyperV.Events
{
    internal class HyperVEventLogWatcher(HyperVManager manager) : IStartable, IDisposable
    {
        const string LOG_NAME = "Microsoft-Windows-Hyper-V-Worker-Admin";

        static string XPath
        {
            get
            {
                var eventPaths = string.Join(" or ", Enum.GetValues<HyperVEventID>().Select(id => "EventID=" + (int)id));

                return $"*[System[Provider[@Name='Microsoft-Windows-Hyper-V-Worker'] and ({eventPaths})]]";
            }
        }

        public required ILogger<HyperVEventLogWatcher> Logger { internal get; init; }

        EventLogWatcher Watcher { get; } = new(new EventLogQuery(LOG_NAME, PathType.LogName, XPath)
        {
            TolerateQueryErrors = true,
        });

        void IStartable.Start()
        {
            Watcher.EventRecordWritten += EventLogWatcher_EventRecordWritten;
            Watcher.Enabled = true;
        }

        private void EventLogWatcher_EventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
        {
            if (args.EventException is null && args.EventRecord is EventRecord record)
            {
                if (FindMachine(record) is HyperVM vm && vm.Semaphore.Wait(0))
                {
                    using (Logger.BeginScope(new Dictionary<string, object> { ["VM"] = vm.Name, ["Event"] = record.Id }))
                    {
                        var eventId = (HyperVEventID)record.Id;
                        var eventLabel = $"{eventId}({record.Id}) @ '{vm.Name}'";

                        try
                        {
                            Logger.LogTrace($"{eventLabel}");

                            switch (eventId)
                            {
                                case HyperVEventID.VM_STARTED:      vm.State = VirtualMachineState.Running;     break;
                                case HyperVEventID.VM_RESTORED:     vm.State = VirtualMachineState.Running;     break;
                                case HyperVEventID.VM_SUSPENDED:    vm.State = VirtualMachineState.Suspended;   break;
                                case HyperVEventID.VM_SHUTDOWN:     vm.State = VirtualMachineState.Stopped;     break;
                                case HyperVEventID.VM_STOPPED:      vm.State = VirtualMachineState.Stopped;     break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, $"{eventLabel}: Could not update VM state");
                        }
                        finally
                        {
                            vm.Semaphore.Release();
                        }
                    }
                }
            }
        }

        private HyperVM? FindMachine(EventRecord record)
        {
            foreach (var prop in record.Properties.Where(prop => prop.Value is string))
            {
                if (manager.Machines.TryGetValue((string)prop.Value, out var machine))
                {
                    return machine;
                }
            }

            return null;
        }

        void IDisposable.Dispose()
        {
            Watcher.Enabled = false;
            Watcher.EventRecordWritten -= EventLogWatcher_EventRecordWritten;
            Watcher.Dispose();
        }
    }
}
