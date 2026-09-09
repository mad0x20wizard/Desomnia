using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal class DuoEventManager : DuoManager
    {
        internal static readonly Version MinVersion = new(1, 5, 7);

        private readonly DuoSessionMonitorConfig _config;
        private readonly object _watcherMutex = new();
        private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false,
        });

        private readonly EventLogWatcher _watcher = new(new EventLogQuery("Application", PathType.LogName, XPath)
        {
            TolerateQueryErrors = true,
        });

        private bool _watching;
        private bool _watcherDisposed;

        public required IProcessManager ProcessManager { private get; init; }

        public DuoEventManager(DuoSessionMonitorConfig config) : base(config)
        {
            _config = config;
        }

        private static string XPath
        {
            get
            {
                var eventPaths = string.Join(" or ", Enum.GetValues<DuoEventID>().Select(id => "EventID=" + (int)id));
                return $"*[System[Provider[@Name='Duo'] and ({eventPaths})]]";
            }
        }

        protected override async Task RunAsync(CancellationToken stoppingToken)
        {
            StartWatching();
            Signal();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var wakeup = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    wakeup.CancelAfter(_config.PollInterval);

                    try
                    {
                        if (!await _signals.Reader.WaitToReadAsync(wakeup.Token))
                            break;

                        while (_signals.Reader.TryRead(out _)) { }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Periodic reconciliation recovers missed events and transient failures.
                    }

                    try
                    {
                        await Reconcile(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // The observed service process ended while its state was being read.
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Could not update Duo service state");
                    }
                }
            }
            finally
            {
                StopWatching();
            }
        }

        private async Task Reconcile(CancellationToken stoppingToken)
        {
            Service.Refresh();
            var status = Service.Status;

            if (status == ServiceControllerStatus.Running)
            {
                if (Service.PID is not uint processId)
                {
                    Logger.LogWarning("The running Duo service has no process ID.");

                    if (IsGenerationInvalidated)
                        await TriggerStopped();

                    return;
                }

                if (ServicePID != processId || IsGenerationInvalidated)
                {
                    if (ServicePID is uint previousProcessId && previousProcessId != processId)
                    {
                        Logger.LogWarning(
                            "Duo service PID changed from {previousPID} to {currentPID} without an observed stop.",
                            previousProcessId,
                            processId);
                    }

                    await TriggerStopped();
                    await Adopt(processId, stoppingToken);
                }
                else
                {
                    await TriggerRefresh(stoppingToken);
                }

                return;
            }

            await TriggerStopped();
        }

        private async Task Adopt(uint processId, CancellationToken stoppingToken)
        {
            var process = ProcessManager[(int)processId];
            var subscription = new DuoProcessSubscription(process, stoppingToken, Signal);

            try
            {
                if (process.HasStopped)
                {
                    subscription.Dispose();
                    Signal();
                    return;
                }

                await TriggerStarted(processId, subscription.Token, subscription);
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
        }

        private void EventLogWatcher_EventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
        {
            using var record = args.EventRecord;

            try
            {
                if (args.EventException is Exception error)
                    Logger.LogWarning(error, "Could not read a Duo event-log record");
            }
            finally
            {
                Signal();
            }
        }

        private void Signal() => _signals.Writer.TryWrite(0);

        private void StartWatching()
        {
            lock (_watcherMutex)
            {
                if (_watching || _watcherDisposed)
                    return;

                _watcher.EventRecordWritten += EventLogWatcher_EventRecordWritten;

                try
                {
                    _watcher.Enabled = true;
                    _watching = true;
                }
                catch
                {
                    _watcher.EventRecordWritten -= EventLogWatcher_EventRecordWritten;
                    throw;
                }
            }
        }

        private void StopWatching()
        {
            lock (_watcherMutex)
            {
                if (!_watching)
                    return;

                try
                {
                    _watcher.Enabled = false;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not stop the Duo event-log watcher cleanly");
                }
                finally
                {
                    _watcher.EventRecordWritten -= EventLogWatcher_EventRecordWritten;
                    _watching = false;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            StopWatching();
            _signals.Writer.TryComplete();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            StopWatching();
            _signals.Writer.TryComplete();
            base.Dispose();

            lock (_watcherMutex)
            {
                if (!_watcherDisposed)
                {
                    _watcherDisposed = true;
                    _watcher.Dispose();
                }
            }
        }

    }
}
