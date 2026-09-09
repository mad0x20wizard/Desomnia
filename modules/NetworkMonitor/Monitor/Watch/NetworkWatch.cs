using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Demand;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Watch
{
    public abstract class NetworkWatch<T> : ResourceMonitor<T> where T : IInspectable
    {
        private bool _cancelIdleActions;

        private long _countBytesIn,  _countPacketsIn;
        private long _countBytesOut, _countPacketsOut;

        public TransmissionThreshold? Threshold { get; set; }

        internal protected virtual Task StartWatch() => Task.CompletedTask;
        internal protected virtual Task StopWatch(bool gracefully)  => Task.CompletedTask;

        protected void ReportNetworkTraffic(long? bytes = null, PacketDirection direction = PacketDirection.Inbound)
        {
            if (direction == PacketDirection.Inbound)
            {
                _countBytesIn += bytes ?? 0;
                _countPacketsIn += 1;
            }
            else
            {
                _countBytesOut += bytes ?? 0;
                _countPacketsOut += 1;
            }

            if (_cancelIdleActions)
            {
                ((IEventSystem)this)[nameof(Idle)].CancelActions();

                _cancelIdleActions = false;
            }
        }

        protected internal virtual void ReportNetworkTraffic(EthernetPacket packet, PacketDirection direction)
        {
            if (packet.Extract<TransportPacket>() is TransportPacket transport)
            {
                ReportNetworkTraffic(transport.PayloadLength, direction); // TODO: sometimes PayloadData is null – PayloadPacket is probably set with some data (e.g. DHCP, port 67/68); need further investigation
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
                // the threshold judges the COMBINED transfer volume — inbound and
                // outbound are tracked separately, but count as one activity signal
                bytes = _countBytesIn + _countBytesOut;

                long packets = _countPacketsIn + _countPacketsOut;

                if (Threshold is TransmissionThreshold speed)
                {
                    double value, minValue;
                    if (speed.ByteUnit is long traffic)
                    {
                        value = bytes;
                        minValue = speed.Amount * traffic;
                    }
                    else
                    {
                        value = packets;
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
                _countBytesIn = _countBytesOut = 0;
                _countPacketsIn = _countPacketsOut = 0;

                _cancelIdleActions = true;
            }
        }
    }
}
