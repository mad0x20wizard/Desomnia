using MadWizard.Desomnia.Service.Duo.Manager.Watcher;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal interface IDuoSessionWatcher
    {
        Task StartWatch(IEnumerable<DuoInstance> instances, CancellationToken token);

        event EventHandler<InstanceSessionChangedEventArgs> SessionChanged;

        void StopWatch();
    }
}
