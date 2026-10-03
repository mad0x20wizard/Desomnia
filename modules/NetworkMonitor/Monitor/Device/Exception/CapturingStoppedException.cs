namespace MadWizard.Desomnia.Network
{
    internal class CapturingStoppedException : CapturingException
    {
        static internal readonly CapturingStoppedException CachedInstance = new();
    }
}
