using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Demand;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Watch
{
    public abstract class NetworkWatch<T> : ResourceMonitor<T> where T : IInspectable
    {
        private long _countBytes;
        private long _countPackets;

        public TransmissionThreshold? Threshold { get; set; }

        internal protected virtual Task StartWatch() => Task.CompletedTask;
        internal protected virtual Task StopWatch(bool gracefully)  => Task.CompletedTask;

        protected void ReportNetworkTraffic(long? bytes = null)
        {
            _countBytes += bytes ?? 0;
            _countPackets += 1;
        }

        protected internal virtual void ReportNetworkTraffic(EthernetPacket packet)
        {
            if (packet.Extract<TransportPacket>() is TransportPacket transport)
            {
                ReportNetworkTraffic(transport.PayloadData?.Length); // TODO: sometimes PayloadData is null – PayloadPacket is probably set with some data (e.g. DHCP, port 67/68); need further investigation
            }
        }

        protected internal async Task TriggerDemandAsync(DemandEvent @event)
        {
            await base.TriggerDemandAsync(@event);
        }

        /**
         * The speed to report alongside a demand, or null where the threshold asked about an
         * amount (or about packets, which have no size to average) – the token then says what was
         * measured in the same terms the question was put in, and says nothing where it cannot.
         */
        protected double? ThresholdRate(TimeSpan since, long bytes)
        {
            if (Threshold is not { ByteUnit: not null, TimeUnit: not null } || since <= TimeSpan.Zero)
                return null;

            return bytes / since.TotalSeconds;
        }

        protected bool HadThresholdTraffic(TimeSpan since, out long bytes)
        {
            try
            {
                bytes = _countBytes; long packets = _countPackets;

                if (Threshold is TransmissionThreshold speed)
                {
                    double value, minValue;
                    if (speed.ByteUnit is long traffic)
                    {
                        value = _countBytes;
                        minValue = speed.Amount * traffic;
                    }
                    else
                    {
                        value = _countPackets;
                        minValue = speed.Amount;
                    }

                    if (speed.TimeUnit is TimeSpan time)
                    {
                        value /= since.TotalMilliseconds;
                        minValue /= time.TotalMilliseconds;
                    }

                    return value >= minValue;
                }

                return packets > 0;
            }
            finally
            {
                _countBytes = 0;
                _countPackets = 0;
            }
        }
    }
}
