using MadWizard.Desomnia.Network.Demand;

namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct DemandOptions
    {
        public DemandSource     Source              { get; init; }
        public TimeSpan         Timeout             { get; init; }
        public bool             Forward             { get; init; }
        public int              Parallel            { get; init; }

        public bool ShouldForward(DemandEvent @event) => Forward && @event.CanBeForwarded;
    }

    public enum DemandSource
    {
        Host, IP
    }
}
