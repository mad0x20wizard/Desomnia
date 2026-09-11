using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class EventWatcher : Watcher, IDisposable
    {
        internal static readonly Version MinVersion = new(1, 5, 7);

        private static string XPath
        {
            get
            {
                var eventPaths = string.Join(" or ", Enum.GetValues<DuoEventID>().Select(id => "EventID=" + (int)id));

                return $"*[System[Provider[@Name='Duo'] and ({eventPaths})]]";
            }
        }

        private EventLogWatcher Watcher { get; } = new(new EventLogQuery("Application", PathType.LogName, XPath)
        {
            TolerateQueryErrors = true,
        });

        public override async Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            var canUseFastPath = !HasAmbiguousNames(instances);

            DuoInstance FindInstance(EventRecord record)
            {
                foreach (var instance in instances)
                    foreach (var item in record.Properties)
                    {
                        if (item.Value.ToString() is string text)
                        {
                            if (text.Contains(instance.Name) || text.Contains(instance.Settings.DisplayName))
                            {
                                return instance;
                            }
                        }
                    }

                throw new KeyNotFoundException();
            }

            async void EventLogWatcher_EventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
            {
                if (args.EventException is null && args.EventRecord is EventRecord record)
                {
                    var eventId = (DuoEventID)record.Id;

                    try
                    {
                        switch (eventId)
                        {
                            case DuoEventID.InstanceStarted:
                                if (!canUseFastPath) goto case DuoEventID.Resuming;
                                NotifyInstanceStatus(FindInstance(record), true);
                                break;

                            case DuoEventID.InstanceError:
                            case DuoEventID.InstanceStopped:
                                if (!canUseFastPath) goto case DuoEventID.Resuming;
                                NotifyInstanceStatus(FindInstance(record), false);
                                break;

                            case DuoEventID.Resuming:
                                await base.RefreshInstances(instances, token);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Could not handle Duo event ({eventId}): " + record.Properties);
                    }
                }
            }

            Watcher.EventRecordWritten += EventLogWatcher_EventRecordWritten;
            Watcher.Enabled = true;

            token.Register(() =>
            {
                Watcher.EventRecordWritten -= EventLogWatcher_EventRecordWritten;
                Watcher.Enabled = false;
            });
        }

        /**
         * Unfortunately the developer of Duo chose to use the Name of the instances as well 
         * as their DisplayName in a mixed fashion. Therefore we have to check both strings,
         * to find out which instance started/stopped exactly.
         * 
         * If one instance name is a substring of another instance we cannot do this and
         * have to discard the contents of the event message entirely, switching to
         * a more brute way of discovering which instance started/stopped.
         */
        private bool HasAmbiguousNames(IEnumerable<DuoInstance> instances)
        {
            foreach (var left in instances)
            {
                foreach (var right in instances.Where(i => i != left))
                {
                    if (left.Settings.Name.Contains(right.Settings.Name))
                        return true;
                    if (left.Settings.DisplayName.Contains(right.Settings.DisplayName))
                        return true;
                }
            }

            return false;
        }

        void IDisposable.Dispose()
        {
            Watcher.Dispose();
        }

        private enum DuoEventID
        {
            // The available ranges are:
            // 1000-1028
            // 1100-1113
            // 1130

            // Regular events
            ServiceStarted = 1000,
            ServiceStopped,
            ServiceError,
            InstanceStarted,
            InstanceStopped,
            InstanceError,
            ProcessStarted,
            ProcessError,
            FeatureConfigurationChanged,
            Suspending,
            Resuming,
            DisplaySettingsChanged,

            // Debug output
            DebugChannel = 1130
        }
    }
}
