namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal interface IDuoManager
    {
        Task<bool> QueryRunningState(DuoInstance instance, CancellationToken token = default);

        Task ChangeState(DuoInstance instance, bool running, CancellationToken token = default);
    }
}
