using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class CompositeWatcher(IEnumerable<IDuoWatcher> watchers) : IDuoWatcher
    {
        public required ILogger<CompositeWatcher> Logger { private get; init; }

        private void LogWatchSignal(IDuoWatcher watcher, WatchSignal signal)
        {
            var message = $"Received signal from {watcher.GetType().Name}";

            if (signal.Instance is DuoInstance instance) lock (instance)
            {
                message += $" for '{instance.Name}'";

                if (instance.Session is ISession session)
                {
                    message += $" [{session.Id}]";
                }

                if (signal.IsRunning is bool running)
                {
                    message += $" = {(running ? "running" : "stopped")}";
                }
            }
               
            Logger.LogTrace(message, watcher.GetType().Name);
        }

        async IAsyncEnumerable<WatchSignal> IDuoWatcher.WatchAsync(IEnumerable<DuoInstance> instances, [EnumeratorCancellation] CancellationToken token)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            using LocalChannel<WatchSignal> channel = Channel.CreateUnbounded<WatchSignal>(new() { SingleReader = true });

            async Task ForwardAsync(IDuoWatcher watcher)
            {
                try
                {
                    await foreach (var signal in watcher.WatchAsync(instances, cancellation.Token))
                    {
                        LogWatchSignal(watcher, signal);

                        channel.Writer.TryWrite(signal);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // watch has ended normally.
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not watch instances with {Watcher}", watcher.GetType().Name);
                }
            }

            async Task WatchAllAsync()
            {
                try
                {
                    await Task.WhenAll(watchers.Select(ForwardAsync));
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }

            var watching = WatchAllAsync();

            try
            {
                await foreach (var signal in channel.Reader.ReadAllAsync(token))
                {
                    yield return signal;
                }
            }
            finally
            {
                await cancellation.CancelAsync();
                await watching;
            }
        }
    }
}
