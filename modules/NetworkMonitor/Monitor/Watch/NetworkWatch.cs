using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Demand;
using PacketDotNet;

namespace MadWizard.Desomnia.Network
{
    public abstract class NetworkWatch<T> : ResourceMonitor<T> where T : IInspectable
    {
        private long _countBytes;
        private long _countPackets;

        public TrafficSpeed? Threshold { get; set; }

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

        protected bool HadThresholdTraffic(TimeSpan since, out long bytes)
        {
            try
            {
                bytes = _countBytes; long packets = _countPackets;

                if (Threshold is TrafficSpeed speed)
                {
                    double value, minValue;
                    if (speed.TrafficUnit is long traffic)
                    {
                        value = _countBytes;
                        minValue = speed.Value * traffic;
                    }
                    else
                    {
                        value = _countPackets;
                        minValue = speed.Value;
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
