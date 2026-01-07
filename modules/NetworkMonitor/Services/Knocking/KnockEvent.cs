using System.Net;

namespace MadWizard.Desomnia.Network.Knocking
{
    public readonly struct KnockEvent(IPAddress source, IPPort? target = null)
    {
        public IPAddress    SourceAddress   { get; init; } = source;
        public IPPort?      TargetPort      { get; init; } = target;

        public DateTime     Time            { get; init; } = DateTime.Now;
    }
}
