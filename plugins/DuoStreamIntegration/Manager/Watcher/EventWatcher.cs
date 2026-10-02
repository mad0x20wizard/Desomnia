using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class EventWatcher : IDuoWatcher, IDisposable
    {
        internal static readonly Version MinVersion = new(1, 5, 7);

        public required ILogger Logger { private get; init; }

        readonly EventLogWatcher _watcher;

        public EventWatcher()
        {
            var query = CreateQuery();

            query.TolerateQueryErrors = true;

            _watcher = new EventLogWatcher(query);
        }

        protected virtual EventLogQuery CreateQuery()
        {
            var eventPaths = string.Join(" or ", Enum.GetValues<DuoEventID>().Select(id => "EventID=" + (int)id));

            var xpath = $"*[System[Provider[@Name='Duo'] and ({eventPaths})]]";

            return new EventLogQuery("Application", PathType.LogName, xpath);
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
        private static void CheckAmbiguousNames(IEnumerable<DuoInstance> instances)
        {
            foreach (var left in instances)
            {
                foreach (var right in instances.Where(i => i != left))
                {
                    if (left.Settings.Name.Contains(right.Settings.Name))
                        throw new AmbiguousInstanceNameException(left.Settings.Name, right.Settings.Name);
                    if (left.Settings.Name.Contains(right.Settings.DisplayName))
                        throw new AmbiguousInstanceNameException(left.Settings.Name, right.Settings.DisplayName);
                    if (left.Settings.DisplayName.Contains(right.Settings.Name))
                        throw new AmbiguousInstanceNameException(left.Settings.DisplayName, right.Settings.Name);
                    if (left.Settings.DisplayName.Contains(right.Settings.DisplayName))
                        throw new AmbiguousInstanceNameException(left.Settings.DisplayName, right.Settings.DisplayName);
                }
            }
        }

        protected virtual DuoInstance FindInstance(IEnumerable<DuoInstance> instances, EventRecord record)
        {
            CheckAmbiguousNames(instances);

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

            throw new KeyNotFoundException("Duo event does not identify a known instance.");
        }

        async IAsyncEnumerable<WatchSignal> IDuoWatcher.WatchAsync(IEnumerable<DuoInstance> instances, [EnumeratorCancellation] CancellationToken token)
        {
            using LocalChannel<WatchSignal> channel = Channel.CreateUnbounded<WatchSignal>(new() { SingleReader = true });

            void EventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
            {
                if (args.EventException is not null)
                {
                    Logger.LogError(args.EventException, "Could not read Duo event log.");
                }
                else if (args.EventRecord is EventRecord record)
                {
                    var eventId = (DuoEventID)record.Id;

                    try
                    {
                        switch (eventId)
                        {
                            case DuoEventID.InstanceStarted:
                                channel.Writer.TryWrite(new() { Instance = FindInstance(instances, record), IsRunning = true });
                                break;

                            case DuoEventID.InstanceError:
                            case DuoEventID.InstanceStopped:
                                channel.Writer.TryWrite(new() { Instance = FindInstance(instances, record), IsRunning = false });
                                break;

                            case DuoEventID.Resuming:
                                channel.Writer.TryWrite(new());
                                break;
                        }
                    }
                    catch (AmbiguousInstanceNameException ex)
                    {
                        Logger.LogWarning(ex, "Unable to determine event instance");

                        channel.Writer.TryWrite(new());
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Could not handle Duo event ({EventId}). ", eventId);
                    }
                    finally
                    {
                        args.EventRecord?.Dispose();
                    }
                }
            }

            try
            {
                _watcher.Enabled = true;
                _watcher.EventRecordWritten += EventRecordWritten;

                await foreach (var signal in channel.Reader.ReadAllAsync(token))
                {
                    yield return signal;
                }
            }
            finally
            {
                _watcher.EventRecordWritten -= EventRecordWritten;
                _watcher.Enabled = false;
            }
        }

        void IDisposable.Dispose()
        {
            _watcher.Dispose();
        }

        protected enum DuoEventID
        {
            // The available ranges are:
            // 1000-1028
            // 1100-1113
            // 1130

            // Regular events
            ServiceStarted  = 1000,
            ServiceStopped  = 1001,
            ServiceError    = 1002,

            InstanceStarted = 1003,
            InstanceStopped = 1004,
            InstanceError   = 1005,

            ProcessStarted,
            ProcessError,

            FeatureConfigurationChanged,
            Suspending,
            Resuming,
            DisplaySettingsChanged,

            // Debug output
            DebugChannel = 1130
        }

        protected class AmbiguousInstanceNameException(params string[] names) : Exception($"Duo instance names [{string.Join(", ", names.Select(n => $"'{n}'"))}] are ambiguous.");
    }
}
