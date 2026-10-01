using MadWizard.Desomnia.Session.Manager;
using Microsoft.Win32;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal abstract class StatusWatcher : BaseWatcher, IDisposable
    {
        public required IDuoManager Manager { protected get; init; }

        CancellationTokenSource? _lifetime;
        Task? _watchTask;
        IDisposable? _sessionSubscription;

        readonly Dictionary<DuoInstance, ISession> _sessions = [];

        readonly Lock _sessionLock = new();
        readonly HashSet<DuoInstance> _pendingStarts = [];

        public override async Task StartWatch(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            StopWatch();
            token.ThrowIfCancellationRequested();

            var watched = instances.ToArray();
            _lifetime = new();

            try
            {
                _sessionSubscription = WatchSessions(watched, _lifetime.Token);

                await RefreshInstances(watched, token);

                token.ThrowIfCancellationRequested();

                _watchTask = WatchAsync(watched, _lifetime.Token);
                _watchTask.ThrowIfFaulted();

                await base.StartWatch(instances, token);
            }
            catch
            {
                StopWatch();
                throw;
            }
        }

        public override void StopWatch()
        {
            if (_lifetime is null)
                return;

            try
            {
                _lifetime.Cancel();
                _watchTask?.GetAwaiter().GetResult();
            }
            finally
            {
                _sessionSubscription?.Dispose();
                _sessionSubscription = null;
                _watchTask = null;
                _lifetime.Dispose();
                _lifetime = null;

                lock (_sessionLock)
                {
                    _pendingStarts.Clear();
                    _sessions.Clear();
                }

                base.StopWatch();
            }
        }

        protected abstract Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token);

        /// <summary>
        /// Refresh all instances using the Duo manager.
        /// </summary>
        protected async Task RefreshInstances(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            foreach (var instance in instances)
            {
                bool running = await Manager.QueryRunningState(instance, token);
                token.ThrowIfCancellationRequested();
                NotifyInstanceStatus(instance, running);
            }
        }

        protected virtual ISession? FindSession(DuoInstance instance)
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Duo\Instances\{instance.Name}");

            if (key?.GetValue("SessionId") is int id && id >= 0)
                return SessionManager.FirstOrDefault(session => session.Id == (uint)id);

            // Older Duo versions identify their RDP sessions through the client name.
            return SessionManager.FirstOrDefault(session =>
                session.ClientName == instance.Name && session.UserName == instance.Settings.UserName);
        }

        protected void NotifyInstanceStatus(DuoInstance instance, bool running)
        {
            lock (_sessionLock)
            {
                if (running)
                {
                    if (FindSession(instance) is ISession session)
                    {
                        _pendingStarts.Remove(instance);
                        NotifySessionChanged(instance, session);
                    }
                    else
                        _pendingStarts.Add(instance);
                }
                else
                {
                    _pendingStarts.Remove(instance);

                    // Duo reports "stopped" before Windows has finished logging off.
                    if (!_sessions.TryGetValue(instance, out var session) || !SessionManager.Contains(session))
                        NotifySessionChanged(instance, null);
                }
            }
        }

        private IDisposable WatchSessions(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            void UserLogon(object? sender, ISession session)
            {
                lock (_sessionLock)
                {
                    if (token.IsCancellationRequested)
                        return;

                    foreach (var instance in _pendingStarts.ToArray())
                        NotifyInstanceStatus(instance, true);
                }
            }

            void UserLogoff(object? sender, ISession session)
            {
                lock (_sessionLock)
                {
                    if (token.IsCancellationRequested)
                        return;

                    foreach (var instance in instances.Where(i => _sessions.GetValueOrDefault(i) == session))
                        NotifySessionChanged(instance, null);
                }
            }

            SessionManager.UserLogon += UserLogon;
            SessionManager.UserLogoff += UserLogoff;

            return new SessionSubscription(() =>
            {
                SessionManager.UserLogon -= UserLogon;
                SessionManager.UserLogoff -= UserLogoff;
            });
        }

        private sealed class SessionSubscription(Action unsubscribe) : IDisposable
        {
            public void Dispose() => unsubscribe();
        }

        private void NotifySessionChanged(DuoInstance instance, ISession? session)
        {
            if (_sessions.GetValueOrDefault(instance) == session)
                return;

            if (session is not null)
                _sessions[instance] = session;
            else
                _sessions.Remove(instance);

            NotifySessionChange(instance, session);
        }

        public virtual void Dispose() => StopWatch();
    }
}
