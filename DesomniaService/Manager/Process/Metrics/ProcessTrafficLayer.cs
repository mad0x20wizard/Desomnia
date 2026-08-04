namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /**
     * The account the traffic meter books into: a decoration around the platform's process that
     * exists only while the active metering is configured, so the process itself never learns
     * what a meter is. From the first booked byte on, the account is the answer; before that –
     * and again once a dead session un-books – the layer beneath answers with the passive
     * approximation, which at least keeps moving.
     */
    public sealed class ProcessTrafficLayer(IProcess process) : ProcessDecorator(process)
    {
        private volatile bool _metered;

        private long _bytesReceived, _bytesSent;

        public override ProcessInputOutput? NetworkData
        {
            get
            {
                if (_metered)
                {
                    return new ProcessInputOutput(Interlocked.Read(ref _bytesReceived), Interlocked.Read(ref _bytesSent));
                }

                return base.NetworkData;
            }
        }

        /// <summary>Books metered bytes into the account – and switches the answer over to it, from the first byte on.</summary>
        internal void BookTraffic(long received = 0, long sent = 0)
        {
            if (received > 0)
                Interlocked.Add(ref _bytesReceived, received);
            if (sent > 0)
                Interlocked.Add(ref _bytesSent, sent);

            _metered = true;
        }

        /**
         * The meter is gone; the account must not go on being the answer, because numbers nobody
         * maintains would read as a process that stopped transferring. The totals themselves
         * stay – a later meter books on top of them, keeping the account monotonic.
         */
        internal void StopMetering() => _metered = false;
    }
}
