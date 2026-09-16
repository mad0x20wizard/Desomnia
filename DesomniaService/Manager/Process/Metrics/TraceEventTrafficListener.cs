using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /**
     * The traffic meter's own kernel trace session: actual TCP and UDP payload bytes, booked
     * straight into the <see cref="ProcessTrafficAccount"/> accounts that asked for them – the
     * same source the Resource Monitor's per-process network column drinks from, and since the
     * IO-counter "approximation" turned out to count device-control chatter rather than the
     * network, the only usable source there is.
     *
     * Deliberately not the process manager's session: that one must run whenever anybody watches
     * process lifetime, while this one costs a packet-sized kernel event for every transfer on
     * the machine – and adding or dropping a keyword means restarting the session, which would
     * lose process starts in the gap. So the meter runs its own, and only while at least one
     * account is subscribed: the first one starts it, the last one leaving stops it – the same
     * bargain the process session strikes with its listeners.
     */
    public sealed class TraceEventTrafficListener : IProcessTrafficMeter, IDisposable
    {
        public required ILogger<TraceEventTrafficListener> Logger { private get; init; }

        /// <summary>The accounts by pid – read lock-free on the pump's event path.</summary>
        readonly ConcurrentDictionary<int, ProcessTrafficAccount> _accounts = [];

        TraceEventSession? _session;

        /// <summary>After a session refused to start, further attempts wait this out: the accounts
        /// retry on every sample, and a broken ETW must not be re-asked at that rate.</summary>
        DateTime _quarantine;

        /**
         * Adds the account and reports whether the meter runs. Idempotent by design: an account
         * whose session died re-subscribes on its next sample, which is also where a torn-down
         * session gets its restart – once per sample, never in a loop.
         */
        public bool StartReading(ProcessTrafficAccount account)
        {
            lock (this)
            {
                _accounts[account.Id] = account;

                if (_session is null && DateTime.UtcNow >= _quarantine)
                {
                    StartSession();
                }

                return _session is not null;
            }
        }

        /**
         * Removes the account – only its own registration, a successor under a reused pid stays –
         * and stops the session behind the last one: with nobody left to book into, every further
         * event would be read and dropped, which is exactly the standing cost this class exists
         * to avoid.
         */
        public void StopReading(ProcessTrafficAccount account)
        {
            lock (this)
            {
                if (_accounts.TryRemove(new KeyValuePair<int, ProcessTrafficAccount>(account.Id, account)) && _accounts.IsEmpty)
                {
                    StopSession();
                }
            }
        }

        private void StartSession()
        {
            try
            {
                Logger.LogDebug("Starting traffic meter session...");

                _session = new("Desomnia::TrafficMeter");
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                /**
                 * The kernel's TCP/UDP events carry the transferred size and – unlike most ETW
                 * events, whose header pid is whoever happened to be running – the owning process
                 * id in their payload, which is what the parser hands out.
                 */
                var kernel = _session.Source.Kernel;

                kernel.TcpIpSend        += data => Book(data.ProcessID, sent: data.size);
                kernel.TcpIpRecv        += data => Book(data.ProcessID, received: data.size);
                kernel.TcpIpSendIPV6    += data => Book(data.ProcessID, sent: data.size);
                kernel.TcpIpRecvIPV6    += data => Book(data.ProcessID, received: data.size);
                kernel.UdpIpSend        += data => Book(data.ProcessID, sent: data.size);
                kernel.UdpIpRecv        += data => Book(data.ProcessID, received: data.size);
                kernel.UdpIpSendIPV6    += data => Book(data.ProcessID, sent: data.size);
                kernel.UdpIpRecvIPV6    += data => Book(data.ProcessID, received: data.size);

                // captured rather than re-read from the field: a stop/start cycle can replace the
                // field before this task's thread ever runs (see the process manager's pump)
                var session = _session;

                Task.Factory.StartNew(() => Process(session), TaskCreationOptions.LongRunning);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "The traffic meter session could not be started");

                _session?.Dispose(); // takes the OS session with it, however far the setup came
                _session = null;

                _quarantine = DateTime.UtcNow.AddMinutes(1);
            }
        }

        /// <summary>Stops the session and un-books every account – numbers nobody maintains must
        /// not linger where they would be read as a process gone quiet.</summary>
        private void StopSession()
        {
            if (_session is not null)
            {
                _session.Source.StopProcessing();
                _session.Stop();

                _session.Dispose();
                _session = null;

                foreach (var account in _accounts.Values)
                {
                    account.StopMetering();
                }

                Logger.LogDebug("Stopped traffic meter session");
            }
        }

        private void Book(int pid, int received = 0, int sent = 0)
        {
            if (_accounts.TryGetValue(pid, out ProcessTrafficAccount? account))
            {
                account.BookTraffic(received, sent);
            }
        }

        #region ETW processing
        private void Process(TraceEventSession session)
        {
            try
            {
                session.Source.Process();
            }
            catch (ObjectDisposedException)
            {
                // superseded by a restart before this thread ever ran; the replacement has its own pump
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Traffic meter session died");

                /**
                 * A dead pump must take its session with it: a counter nobody updates is a 0-byte
                 * delta, which reads as a group gone idle – the one lie the metering must never
                 * tell. Torn down, the accounts answer null instead (the watch assumes demand)
                 * and their next sample is the restart; restarting from here would just spin if
                 * whatever killed the pump is not done killing.
                 */
                lock (this)
                {
                    if (_session == session)
                    {
                        StopSession();
                    }
                }
            }
        }
        #endregion

        public void Dispose()
        {
            lock (this)
            {
                StopSession();
            }
        }
    }
}
