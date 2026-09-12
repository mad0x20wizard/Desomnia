using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class EventWatcher : BaseWatcher, IDisposable
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

        private Channel<Signal> _channel = Channel.CreateUnbounded<Signal>(new() { SingleReader = true });

        private EventLogWatcher Watcher { get; } = new(new EventLogQuery("Application", PathType.LogName, XPath)
        {
            TolerateQueryErrors = true,
        });

        public override async Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            if (!HasAmbiguousNames(instances) is bool canUseFastPath && !canUseFastPath)
            {
                Logger.LogWarning("Duo instance names and display names are ambiguous; cannot use fast path");
            }

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

                throw new KeyNotFoundException("Duo event does not identify a known instance.");
            }

            Watcher.EventRecordWritten += EventRecordWritten;

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
                                if (!canUseFastPath) goto case DuoEventID.Resuming;
                                _channel.Writer.TryWrite(new() { Instance = FindInstance(record), Running = true });
                                break;

                            case DuoEventID.InstanceError:
                            case DuoEventID.InstanceStopped:
                                if (!canUseFastPath) goto case DuoEventID.Resuming;
                                _channel.Writer.TryWrite(new() { Instance = FindInstance(record), Running = false});
                                break;

                            case DuoEventID.Resuming:
                                _channel.Writer.TryWrite(new());
                                break;
                        }
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
                Watcher.Enabled = true;

                await foreach (var signal in _channel.Reader.ReadAllAsync(token))
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        if (signal.Instance is DuoInstance instance)
                        {
                            NotifyInstanceStatus(instance, signal.Running);
                        }
                        else
                        {
                            await base.RefreshInstances(instances, token);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        Logger.LogError(ex, "Could not handle event log signal: {Signal}", signal);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Normal shutdown; the event subscription is released by the iterator.
            }
            finally
            {
                _channel.Writer.TryComplete();

                Watcher.EventRecordWritten -= EventRecordWritten;
                Watcher.Enabled = false;
            }
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
        private static bool HasAmbiguousNames(IEnumerable<DuoInstance> instances)
        {
            foreach (var left in instances)
            {
                foreach (var right in instances.Where(i => i != left))
                {
                    if (left.Settings.Name.Contains(right.Settings.Name))
                        return true;
                    if (left.Settings.Name.Contains(right.Settings.DisplayName))
                        return true;
                    if (left.Settings.DisplayName.Contains(right.Settings.Name))
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

        private record class Signal
        {
            internal DuoInstance? Instance { get; init; }

            internal bool Running { get; init; }

            public override string ToString()
            {
                if (Instance is not null)
                {
                    return Instance.ToString() + " -> " + Running;
                }
                else
                {
                    return "Refresh";
                }
            }
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
