using MadWizard.Desomnia.Service.Duo.Manager.Watcher;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal interface IDuoInstanceWatcher
    {
        Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken stoppingToken);

        event EventHandler<InstanceStatusChangedEventArgs> StatusChanged;
    }
}
