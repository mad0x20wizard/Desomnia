using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network
{
    public class NetworkServiceUsage(NetworkService service, long bytes) : NetworkUsage(bytes)
    {
        public string Name { get; set; } = service.Name;

        /// <summary>The service, and – where a speed was what the threshold asked about – how fast it was going.</summary>
        public override string ToString() => Rate is double rate ? $"{Name}@{IOFormat.BitsPerSecond(rate)}" : Name;
    }
}
