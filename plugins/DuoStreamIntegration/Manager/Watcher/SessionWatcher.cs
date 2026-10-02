using MadWizard.Desomnia.Ressource.Events;
using MadWizard.Desomnia.Session;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class SessionWatcher : IDuoWatcher
    {
        async IAsyncEnumerable<WatchSignal> IDuoWatcher.WatchAsync(IEnumerable<DuoInstance> instances, [EnumeratorCancellation] CancellationToken token)
        {
            using LocalChannel<WatchSignal> channel = Channel.CreateUnbounded<WatchSignal>(new() { SingleReader = true });

            void Instance_TrackingStopped(object? sender, InspectableEventArgs<Resource> args)
            {
                if (args.Inspectable is SessionWatch eatch)
                {
                    channel.Writer.TryWrite(new() { Instance = sender as DuoInstance });
                }
            }

            foreach (var instance in instances)
            {
                instance.TrackingStopped += Instance_TrackingStopped;
            }

            try
            {
                await foreach (var signal in channel.Reader.ReadAllAsync(token))
                {
                    yield return signal;
                }
            }
            finally
            {
                foreach (var instance in instances)
                {
                    instance.TrackingStopped -= Instance_TrackingStopped;
                }
            }
        }
    }
}
