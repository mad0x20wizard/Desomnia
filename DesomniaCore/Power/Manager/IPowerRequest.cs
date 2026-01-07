namespace MadWizard.Desomnia.Power.Manager
{
    public interface IPowerRequest : IDisposable
    {
        public string Name { get; }
        public string? Reason { get; }
    }
}
