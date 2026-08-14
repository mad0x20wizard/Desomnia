using MadWizard.Desomnia.LaunchDaemon.Native;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /**
     * The traffic meter on macOS: the kernel's own per-flow byte counters, read off the private
     * "com.apple.network.statistics" control and added up per process.
     *
     * Where the Windows meter is told – a trace session pushes an event per transfer, machine-wide
     * and continuously, for as long as it runs – this one asks, and only when somebody wants an
     * answer. A full poll of every flow on the machine costs about a millisecond and a half, and
     * between polls nothing runs at all, so the standing cost of metering here is genuinely zero
     * rather than merely small. It is also why the poll is machine-wide and not per process: the
     * kernel answers for everything in one request, so watching one process costs what watching
     * fifty does.
     *
     * The accounting is the part that has to be right. The kernel counts per *flow*, and a flow's
     * counters cease to exist when its connection closes – so a meter that added up the flows a
     * process currently has would watch its total fall when a connection ended, which reads as a
     * process that stopped transferring: the one lie this metering must never tell. Instead every
     * flow's counters are remembered and only their growth is booked, into an account that never
     * decreases. Bytes carried between the last poll and a close are not lost either: the kernel
     * sends a flow's final counts before announcing its removal, and those wait in the socket
     * buffer until the next poll reads them.
     */
    public sealed class NtStatTrafficListener : IProcessMetricSupport, IProcessTrafficMeter, IDisposable
    {
        public required ILogger<NtStatTrafficListener> Logger { private get; init; }

        /// <summary>The accounts, by the pid whose flows they answer for.</summary>
        readonly Dictionary<int, ProcessTrafficAccount> _accounts = [];

        /// <summary>Last counters per flow, kept only for flows an account is waiting on.</summary>
        readonly Dictionary<ulong, Flow> _flows = [];

        sealed class Flow
        {
            public int Pid;
            public ulong Received, Sent;
        }

        NtStatSession? _session;
        uint? _controlId;

        DateTime _polled, _quarantine;
        int _faults;
        bool _warnedAboutDrops;

        /**
         * How stale an answer may be before a sample pays for a poll of its own.
         *
         * Every watched process asks within the same monitor cycle, and one poll answers for all of
         * them – this is what collapses those asks into it. Well below any cycle the monitor runs,
         * so it never makes an answer older than the cycle that asked for it.
         */
        static readonly TimeSpan Freshness = TimeSpan.FromMilliseconds(250);

        #region Compatibility
        bool? _compatible;

        /**
         * Whether this kernel can be metered at all – asked once, when the configuration is turned
         * into watches, and answered by talking to it rather than by guessing from the OS version.
         *
         * There is no version handshake to use: the protocol's revision constant has been frozen
         * for years while the structures moved underneath it. What the kernel does state is the
         * size of its own descriptors, in the length of every message it sends, and a size this
         * build has no layout for is refused here – so a configured minTraffic fails at startup
         * with a message naming what was found, instead of quietly metering the wrong bytes.
         */
        ProcessMetric IProcessMetricSupport.SupportedMetrics
        {
            get
            {
                lock (this)
                {
                    return (_compatible ??= Probe()) ? ProcessMetric.Traffic : ProcessMetric.None;
                }
            }
        }

        private bool Probe()
        {
            // its own socket, closed again straight away: this is asked when the configuration is
            // built, which is not the same as anybody wanting the metric measured
            using var session = CreateSession();

            if (session is null)
                return false;

            var layouts = new Dictionary<uint, int>();
            var unknown = new Dictionary<uint, int>();

            var result = session.Poll(message =>
            {
                if (NtStat.TypeOf(message) != NtStat.MSG_SRC_UPDATE || message.Length <= NtStat.SRC_UPDATE_FIXED)
                    return;

                var provider = NtStat.ReadU32(message, NtStat.UPDATE_PROVIDER_OFFSET);
                var size = message.Length - NtStat.SRC_UPDATE_FIXED;

                (NtStatLayout.For(provider, size) is null ? unknown : layouts)[provider] = size;
            });

            if (result.Error is int errno)
            {
                Logger.LogError("The traffic meter could not poll the kernel: {error}", NtStat.ErrnoName(errno));

                return false;
            }

            foreach (var (provider, size) in unknown)
            {
                Logger.LogError("The kernel reports {provider} flows in {size} bytes, which this build has no layout for (it knows {known})",
                                NtStat.ProviderName(provider), size, NtStatLayout.Describe(provider));
            }

            if (unknown.Count > 0)
                return false;

            foreach (var (provider, size) in layouts)
            {
                Logger.LogDebug("Traffic meter: {provider} descriptors are {layout}", NtStat.ProviderName(provider), NtStatLayout.For(provider, size));
            }

            // an idle machine with no flow at all says nothing either way; metering it is harmless
            // and the next poll that finds one validates it against the same table
            return true;
        }
        #endregion

        #region Accounts
        public bool StartReading(ProcessTrafficAccount account)
        {
            lock (this)
            {
                if (!EnsureSession())
                    return false;

                _accounts[account.Id] = account;

                return true;
            }
        }

        /**
         * Drops the account – only its own registration, a successor under a reused pid stays – and
         * closes the socket behind the last one: with nobody left to book into, a poll would read
         * the whole machine's flows to throw them away.
         */
        public void StopReading(ProcessTrafficAccount account)
        {
            lock (this)
            {
                if (_accounts.TryGetValue(account.Id, out var registered) && registered == account)
                {
                    _accounts.Remove(account.Id);

                    // its flows are nobody's business now, and their references will be handed out again
                    foreach (var reference in _flows.Where(entry => entry.Value.Pid == account.Id).Select(entry => entry.Key).ToList())
                    {
                        _flows.Remove(reference);
                    }

                    if (_accounts.Count == 0)
                        CloseSession();
                }
            }
        }

        /**
         * Brings every account up to date, at most once per <see cref="Freshness"/>.
         *
         * Called by each account as it is read, which is what makes the metering demand-driven: a
         * monitor cycle that samples ten watched processes polls once, and a cycle that samples
         * none does not poll at all.
         */
        public void Refresh()
        {
            lock (this)
            {
                if (_session is null || DateTime.UtcNow - _polled < Freshness)
                    return;

                _polled = DateTime.UtcNow;

                var result = _session.Poll(Book);

                if (!result.Faulted)
                {
                    _faults = 0;

                    return;
                }

                /**
                 * A single fault is survivable and costs nothing: the counters are cumulative, so a
                 * truncated or unanswered poll delays bytes rather than losing them, and the next
                 * poll reads the same totals again. A meter that keeps faulting is another matter –
                 * its accounts would go on answering with numbers nobody maintains, which reads as
                 * a machine gone quiet, so it is torn down and the accounts fall back to null.
                 */
                Logger.LogDebug("Traffic meter poll failed ({error})", result.Error is int errno ? NtStat.ErrnoName(errno) : "no answer");

                if (++_faults >= 3)
                {
                    Logger.LogWarning("Stopping the traffic meter after {faults} failed polls", _faults);

                    CloseSession();

                    _quarantine = DateTime.UtcNow.AddMinutes(1);
                }
            }
        }
        #endregion

        #region Booking
        private void Book(ReadOnlySpan<byte> message)
        {
            switch (NtStat.TypeOf(message))
            {
                case NtStat.MSG_SRC_UPDATE:
                    BookUpdate(message);
                    break;

                // the legacy shape of a goodbye: counts for a flow already known, no descriptor
                case NtStat.MSG_SRC_COUNTS when message.Length >= NtStat.SRC_COUNTS_SIZE:
                    if (_flows.TryGetValue(NtStat.SourceRefOf(message), out var closing))
                        BookCounts(closing, message);
                    break;

                case NtStat.MSG_SRC_REMOVED:
                    Forget(message);
                    break;
            }
        }

        private void BookUpdate(ReadOnlySpan<byte> message)
        {
            if (message.Length <= NtStat.SRC_UPDATE_FIXED)
                return;

            var provider = NtStat.ReadU32(message, NtStat.UPDATE_PROVIDER_OFFSET);
            var reference = NtStat.SourceRefOf(message);

            if (NtStatLayout.For(provider, message.Length - NtStat.SRC_UPDATE_FIXED) is not NtStatLayout layout)
                return; // refused at the probe; a shape that appears later is skipped, never guessed at

            var descriptor = message[NtStat.SRC_UPDATE_FIXED..];

            int pid = NtStat.ReadI32(descriptor, layout.Pid);
            int effective = NtStat.ReadI32(descriptor, layout.EffectivePid);

            // a delegated socket does its work for somebody else, and it is that somebody whose
            // demand this is – a daemon transferring on an app's behalf must not book it as its own
            if (effective > 0)
                pid = effective;

            if (!_flows.TryGetValue(reference, out var flow))
            {
                // only flows somebody is waiting on are remembered: the machine has hundreds, and a
                // process that subscribes later is meant to count from its subscription anyway
                if (!_accounts.ContainsKey(pid))
                    return;

                _flows[reference] = flow = new Flow { Pid = pid };
            }
            else if (flow.Pid != pid)
            {
                // the reference was handed out again for another process' flow
                flow.Pid = pid;
                flow.Received = flow.Sent = 0;
            }

            BookCounts(flow, message);
        }

        private void BookCounts(Flow flow, ReadOnlySpan<byte> message)
        {
            var counts = NtStat.UPDATE_COUNTS_OFFSET;

            var received = Advance(ref flow.Received, NtStat.ReadU64(message, counts + NtStat.COUNTS_RXBYTES));
            var sent = Advance(ref flow.Sent, NtStat.ReadU64(message, counts + NtStat.COUNTS_TXBYTES));

            if ((received > 0 || sent > 0) && _accounts.TryGetValue(flow.Pid, out var account))
            {
                account.BookTraffic(received, sent);
            }
        }

        /// <summary>The growth since the last poll. Clamped, because a reused source reference would
        /// otherwise book a negative into a total that only ever rises.</summary>
        private static long Advance(ref ulong previous, ulong current)
        {
            var delta = current > previous ? current - previous : 0;

            previous = current;

            return (long)delta;
        }

        private void Forget(ReadOnlySpan<byte> message)
        {
            _flows.Remove(NtStat.SourceRefOf(message));

            /**
             * The kernel could not hand over this flow's final counts – its bytes since the last
             * poll are gone. That only happens when the socket buffer filled between two polls, so
             * it is worth saying once: the cure is a shorter monitor cycle, and the alternative is
             * a process that transferred looking slightly quieter than it was.
             */
            if ((NtStat.FlagsOf(message) & NtStat.FLAG_CLOSED_AFTER_DROP) != 0 && !_warnedAboutDrops)
            {
                _warnedAboutDrops = true;

                Logger.LogWarning("The kernel dropped a flow's final traffic counts – some bytes go unmeasured between polls this far apart");
            }
        }
        #endregion

        #region Session
        private bool EnsureSession()
        {
            if (_session is not null)
                return true;

            if (DateTime.UtcNow < _quarantine)
                return false;

            _session = CreateSession();

            if (_session is null)
            {
                _quarantine = DateTime.UtcNow.AddMinutes(1);

                return false;
            }

            // whatever the kernel already counted is not this meter's to report: the accounts start
            // where the subscription does, so the first poll only establishes the baseline
            _polled = DateTime.MinValue;

            return true;
        }

        /// <summary>A socket subscribed to every flow provider that would have it, or null with the reason logged.</summary>
        private NtStatSession? CreateSession()
        {
            if (_controlId is null)
            {
                _controlId = NtStat.ResolveControlId(out var failure);

                if (_controlId is null)
                {
                    Logger.LogError("The traffic meter could not reach the kernel's statistics control: {failure}", failure);

                    return null;
                }
            }

            var session = NtStatSession.Open(_controlId.Value, out var reason);

            if (session is null)
            {
                Logger.LogError("The traffic meter could not open its statistics socket: {reason}", reason);

                return null;
            }

            var subscribed = 0;

            foreach (var provider in NtStat.TrafficProviders)
            {
                var errno = session.Subscribe(provider);

                if (errno == 0)
                    subscribed++;
                else
                    Logger.LogWarning("The traffic meter was refused {provider}: {error}", NtStat.ProviderName(provider), NtStat.ErrnoName(errno));
            }

            if (subscribed == 0)
            {
                Logger.LogError("The traffic meter was refused every flow provider – nothing can be measured");

                session.Dispose();

                return null;
            }

            Logger.LogDebug("Traffic meter watching {subscribed} of {providers} flow providers", subscribed, NtStat.TrafficProviders.Length);

            return session;
        }

        /// <summary>Closes the socket and un-books every account – numbers nobody maintains must not
        /// linger where they would be read as a process gone quiet.</summary>
        private void CloseSession()
        {
            if (_session is not null)
            {
                _session.Dispose();
                _session = null;

                _flows.Clear();

                foreach (var account in _accounts.Values)
                {
                    account.StopMetering();
                }

                Logger.LogDebug("Stopped the traffic meter");
            }
        }
        #endregion

        public void Dispose()
        {
            lock (this)
            {
                CloseSession();
            }
        }
    }
}
