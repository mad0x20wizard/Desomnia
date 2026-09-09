namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public sealed class DuoLifecycleEventArgs(uint pid, IReadOnlyList<DuoInstance> instances) : EventArgs
    {
        public uint ProcessId { get; } = pid;

        public IReadOnlyList<DuoInstance> Instances { get; } = instances;
    }
}
