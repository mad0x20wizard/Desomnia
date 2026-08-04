using MadWizard.Desomnia.Processes.Manager.Native;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes.Manager
{
    /// <summary>
    /// One epoll instance and one thread blocked on it, reporting process exits the moment they
    /// happen instead of at the next poll. The Linux counterpart of the macOS kqueue watcher, and
    /// it answers the same question the same way: a pidfd becomes readable when its process becomes
    /// a zombie, not when it is reaped, so a process nobody collects is still reported gone.
    ///
    /// The cost is one descriptor per watched process, which is why <see cref="_budget"/> exists.
    /// Watching only the processes some watch actually names would remove the need for it.
    /// </summary>
    internal sealed class EpollProcessExitWatcher : IDisposable
    {
        /// <summary>The cookie of the wake-up counter – far outside any pid, and lopsided on
        /// purpose: a byte of it in the wrong place is what a mislaid event layout looks like.</summary>
        private const ulong Wakeup = 0xFEEDFACE_DEADBEEF;

        private const int MaxEvents = 16;

        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<int, (int Descriptor, Action OnExit)> _watched = [];

        private readonly int _epoll;
        private readonly int _wakeup;
        private readonly int _budget;

        private readonly Thread _thread;

        private volatile bool _stopping;
        private bool _exhausted;

        public EpollProcessExitWatcher(ILogger logger, int budget = 256)
        {
            _logger = logger;
            _budget = budget;

            _epoll = Epoll.Open();

            try
            {
                _wakeup = Epoll.OpenEvent();

                if (!Epoll.TryWatch(_epoll, _wakeup, Wakeup))
                    throw new InvalidOperationException($"watching the wake-up counter failed: errno {Marshal.GetLastPInvokeError()}");

                VerifyEventLayout();
            }
            catch
            {
                Close();

                throw;
            }

            _thread = new Thread(Run) { Name = nameof(EpollProcessExitWatcher), IsBackground = true };
            _thread.Start();

            _logger.LogDebug("Watching for process exits through epoll");
        }

        /**
         * Proves the event layout before anything depends on it.
         *
         * struct epoll_event is packed on x86_64 and padded everywhere else, and getting that wrong
         * does not fail loudly: epoll still reports events, the cookies just come back as rubbish,
         * and the daemon would quietly report the exit of a process that never existed. So the
         * wake-up counter is fired once at startup and its cookie checked byte for byte.
         */
        private void VerifyEventLayout()
        {
            var events = new byte[MaxEvents * Epoll.EventSize];

            Epoll.Signal(_wakeup);

            int count = Epoll.Wait(_epoll, events, timeout: 1000);

            if (count != 1 || Epoll.EventData(events, 0) != Wakeup)
                throw new InvalidOperationException($"epoll reported {count} event(s) with an unrecognisable cookie – struct epoll_event is laid out differently here than assumed");

            Epoll.Drain(_wakeup);
        }

        /// <summary>
        /// Asks to be told when <paramref name="pid"/> ends. A pid that has already ended is not an
        /// error: the descriptor opens anyway and is readable at once, so the exit is still reported.
        /// </summary>
        public void Watch(int pid, Action onExit)
        {
            if (_watched.Count >= _budget)
            {
                Exhausted("the watch budget of {budget} descriptors is used up", _budget);

                return;
            }

            int descriptor = Epoll.OpenProcess(pid);

            if (descriptor < 0)
            {
                int error = Marshal.GetLastPInvokeError();

                if (error is Epoll.EMFILE or Epoll.ENFILE)
                    Exhausted("no file descriptors left (errno {error})", error);
                else if (error != Epoll.ESRCH)
                    _logger.LogTrace("Not watching {pid} for exit: errno {error}", pid, error);

                return;
            }

            if (!_watched.TryAdd(pid, (descriptor, onExit)))
            {
                // a second watch for a pid already covered – both lanes of a create race end up
                // here. The first registration stands and only the duplicate descriptor closes:
                // removing the standing entry would orphan a registered pidfd, which stays
                // readable for good and would spin this thread at full tilt (see Deliver).
                Epoll.Close(descriptor);
            }
            else if (!Epoll.TryWatch(_epoll, descriptor, (ulong)pid))
            {
                _watched.TryRemove(pid, out _);

                Epoll.Close(descriptor);
            }
        }

        /// <summary>Says so once, and then stops saying it every time a process starts.</summary>
        private void Exhausted(string reason, params object[] arguments)
        {
            if (_exhausted)
                return;

            _exhausted = true;

            _logger.LogWarning($"Process exits fall back to the poll interval: {reason}", arguments);
        }

        private void Run()
        {
            var events = new byte[MaxEvents * Epoll.EventSize];

            while (!_stopping)
            {
                int count = Epoll.Wait(_epoll, events);

                if (count < 0)
                {
                    int error = Marshal.GetLastPInvokeError();

                    // the runtime signals its own threads, so this is routine rather than exceptional
                    if (error == Epoll.EINTR)
                        continue;

                    _logger.LogWarning("epoll_wait failed: errno {error}. Process exits fall back to the poll interval.", error);

                    return;
                }

                for (int i = 0; i < count && !_stopping; i++)
                {
                    ulong cookie = Epoll.EventData(events, i);

                    if (cookie == Wakeup)
                        Epoll.Drain(_wakeup);
                    else
                        Deliver((int)cookie);
                }
            }
        }

        private void Deliver(int pid)
        {
            if (!_watched.TryRemove(pid, out var watched))
                return;

            // A pidfd stays readable for good once its process has gone, and epoll reports readiness
            // on every pass – so leaving this registered would not merely leak a descriptor, it
            // would spin this thread at full tilt for as long as the daemon runs.
            Epoll.Unwatch(_epoll, watched.Descriptor);
            Epoll.Close(watched.Descriptor);

            _exhausted = false; // a descriptor came back; a future shortage is worth reporting again

            try
            {
                // deliberately on this thread: reporting exits in the order the kernel gave them
                // matters more than reporting them concurrently
                watched.OnExit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reporting the exit of {pid} failed", pid);
            }
        }

        public void Dispose()
        {
            _stopping = true;

            Epoll.Signal(_wakeup);

            _thread?.Join(TimeSpan.FromSeconds(5));

            Close();
        }

        private void Close()
        {
            foreach (var pid in _watched.Keys)
            {
                if (_watched.TryRemove(pid, out var watched))
                    Epoll.Close(watched.Descriptor);
            }

            if (_wakeup > 0)
                Epoll.Close(_wakeup);

            if (_epoll > 0)
                Epoll.Close(_epoll);
        }
    }
}
