using MadWizard.Desomnia.Network.Capture;
using Autofac;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Diagnostics;
using System.Net;

namespace MadWizard.Desomnia.Network.Demand
{
    public class DemandRequest : DemandRequestBuffer, IDisposable
    {
        private Stopwatch _stopwatch = Stopwatch.StartNew();

        public required ILifetimeScope Scope { private get; init; }

        public int Number { get; internal set; }

        public required NetworkHost Host { get; init; }

        public NetworkHost? SourceHost => Host.Network[SourceAddress];

        public IPAddress SourceAddress { get; init; }
        public IPAddress TargetAddress { get; init; }

        public IPService? Service { get; set; }

        public TimeSpan Duration => _stopwatch.Elapsed;

        internal DemandResult? Result { get; set; }

        public DemandRequest(INetworkCapture trigger)
        {
            SourceAddress = trigger.SourceAddress ?? throw new ArgumentException("Trigger packet does not contain a source IP address.");
            TargetAddress = trigger.TargetAddress ?? throw new ArgumentException("Trigger packet does not contain a target IP address.");
        }

        void IDisposable.Dispose()
        {
            Scope.Dispose();
        }

        public override string ToString()
        {
            return $"DemandRequest#{Number}";
        }
    }

    public enum DemandResult
    {
        Valid,
        Filtered,
        Timeout
    }
}
