using Autofac.Features.Decorators;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /**
     * The account the traffic meter books into: a decoration around the platform's process, so
     * the process itself never learns what a meter is. The metering is demand-driven – the first
     * NetworkData sample subscribes this account to the meter, which runs only while subscribed
     * accounts exist – because there is no passive fallback to be cheap with: on Windows the
     * IO-counter approximation measured device-control chatter rather than the network, and on
     * macOS there is no public counter at all. Answering wrong is worse than answering null
     * (which the watch reads as "assume demand").
     *
     * Subscribed, the account is the answer – including an honest zero for a process that
     * transfers nothing. A dead meter un-books it (<see cref="StopMetering"/>) and the next
     * sample is the retry; the totals themselves stay, a later session books on top of them,
     * keeping the account monotonic.
     *
     * Both directions are kept apart all the way through, even though no threshold distinguishes
     * them today: a stream client is nearly all inbound and a backup nearly all outbound, so the
     * day a direction-specific threshold is wanted, the numbers are already there.
     */
    public sealed class ProcessTrafficAccount(IProcess process, IDecoratorContext context) : ProcessDecorator(process, context)
    {
        public required IProcessTrafficMeter Meter { private get; init; }

        private volatile bool _metered;
        private bool _attached, _detached;

        private long _bytesReceived, _bytesSent;

        public override ProcessInputOutput? NetworkData
        {
            get
            {
                if (_metered || TryMeter())
                {
                    // deliberately outside the lock above: a polling meter does its reading here,
                    // and it books into accounts of its own while it does
                    Meter.Refresh();

                    return new ProcessInputOutput(Interlocked.Read(ref _bytesReceived), Interlocked.Read(ref _bytesSent));
                }

                return base.NetworkData; // null – the watch fails open rather than reading a dead meter as idle
            }
        }

        /**
         * The subscription, taken out on first demand and re-taken after a meter died: every
         * sample of an un-metered account is also the retry, so recovery needs no timer and
         * costs one locked call per monitor cycle.
         */
        private bool TryMeter()
        {
            lock (this)
            {
                if (_detached)
                    return false;

                if (!_attached)
                {
                    _attached = true;

                    // the stop is the subscription's natural end – a stopped process would
                    // otherwise hold the meter open for nothing until the final teardown.
                    // Dispose covers whatever a stop never reaches: race losers, the manager's
                    // own teardown on shutdown.
                    Stopped += (sender, args) => Detach();
                }

                if (HasStopped) // stopped before (or while) the hook went in – the event will not fire for us
                {
                    Detach();

                    return false;
                }

                return _metered = Meter.StartReading(this);
            }
        }

        /// <summary>Books metered bytes into the account. Only the bytes: whether the account is
        /// the answer is the subscription's business, not a byte's – a handler caught mid-flight
        /// after a meter died must not resurrect a meter nobody runs.</summary>
        public void BookTraffic(long received = 0, long sent = 0)
        {
            if (received > 0)
                Interlocked.Add(ref _bytesReceived, received);
            if (sent > 0)
                Interlocked.Add(ref _bytesSent, sent);
        }

        /**
         * The meter is gone; the account must not go on being the answer, because numbers nobody
         * maintains would read as a process that stopped transferring. The totals stay – a later
         * subscription books on top of them, keeping the account monotonic.
         */
        public void StopMetering() => _metered = false;

        private void Detach()
        {
            lock (this)
            {
                if (!_detached)
                {
                    _detached = true;
                    _metered = false;

                    Meter.StopReading(this); // a no-op for an account that never got to subscribe
                }
            }
        }

        public override void Dispose()
        {
            Detach();

            base.Dispose();
        }
    }
}
