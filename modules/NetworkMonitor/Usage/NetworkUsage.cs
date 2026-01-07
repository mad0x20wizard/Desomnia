namespace MadWizard.Desomnia.Network
{
    public abstract class NetworkUsage(long bytes) : UsageToken
    {
        public long Bytes => bytes;

    }
}
