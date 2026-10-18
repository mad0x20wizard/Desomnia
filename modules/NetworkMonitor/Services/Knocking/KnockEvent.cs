using System.Net;

namespace MadWizard.Desomnia.Network.Knocking
{
    public readonly struct KnockEvent(IPAddress remote, IPService? target = null)
    {
        public IPAddress    RemoteAddress   { get; init; } = remote;
        public IPService?      TargetPort      { get; init; } = target;

        public DateTime     Time            { get; init; } = DateTime.Now;
    }
}
