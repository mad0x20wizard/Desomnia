namespace MadWizard.Desomnia.Network.Device
{
    internal class CapturingStoppedException : CapturingException
    {
        static internal readonly CapturingStoppedException CachedInstance = new();
    }
}
