using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class RegistryWatcher : BaseWatcher, IDisposable
    {
        public required ISessionManager SessionManager { private get; init; }

        CancellationTokenSource? _lifetime;
        Task? _watchTask;
        Channel<Signal>? _channel;

        readonly Dictionary<DuoInstance, KeyWatch> _watches = [];

        protected virtual RegistryKey OpenInstanceKey(DuoInstance instance) =>
            Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Duo\Instances\{instance.Name}", writable: true)
                ?? throw new FileNotFoundException($"Duo instance key not found: {instance.Name}");

        public override Task StartWatch(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            StopWatch();
            token.ThrowIfCancellationRequested();

            _lifetime = new();
            _channel = Channel.CreateUnbounded<Signal>(new() { SingleReader = true });

            SessionManager.UserLogon += SessionManager_UserLogon;
            SessionManager.UserLogoff += SessionManager_UserLogoff;

            try
            {
                foreach (var instance in instances)
                {
                    token.ThrowIfCancellationRequested();

                    var channel = _channel;
                    var watch = new KeyWatch(OpenInstanceKey(instance), () => channel.Writer.TryWrite(new(instance)));
                    _watches.Add(instance, watch);

                    watch.Arm();
                    RefreshSession(instance, watch.Key);
                }

                token.ThrowIfCancellationRequested();

                _watchTask = WatchAsync(_lifetime.Token);
                _watchTask.ThrowIfFaulted();

                return base.StartWatch(instances, token);
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

            SessionManager.UserLogon -= SessionManager_UserLogon;
            SessionManager.UserLogoff -= SessionManager_UserLogoff;

            try
            {
                _lifetime.Cancel();
                _watchTask?.GetAwaiter().GetResult();
            }
            finally
            {
                foreach (var watch in _watches.Values)
                    watch.Dispose();

                _watches.Clear();
                _channel?.Writer.TryComplete();
                _channel = null;
                _watchTask = null;
                _lifetime.Dispose();
                _lifetime = null;
            }
        }

        private void SessionManager_UserLogon(object? sender, ISession session) =>
            _channel?.Writer.TryWrite(new());

        private void SessionManager_UserLogoff(object? sender, ISession session) =>
            _channel?.Writer.TryWrite(new(LoggedOff: session));

        private async Task WatchAsync(CancellationToken token)
        {
            try
            {
                await foreach (var signal in _channel!.Reader.ReadAllAsync(token))
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        if (signal.LoggedOff is ISession session)
                        {
                            foreach (var (instance, watch) in _watches.Where(pair => pair.Value.Session == session))
                            {
                                // Temporary workaround: Duo currently leaves SessionId behind on logoff.
                                // Do not erase a newer session that Duo may already have written.
                                try
                                {
                                    if (ReadSessionId(watch.Key) == session.Id)
                                        watch.Key.DeleteValue("SessionId", throwOnMissingValue: false);
                                }
                                finally
                                {
                                    NotifySessionChanged(instance, null);
                                }
                            }
                        }
                        else if (signal.Instance is DuoInstance instance)
                        {
                            var watch = _watches[instance];
                            watch.Arm();
                            RefreshSession(instance, watch.Key);
                        }
                        else
                        {
                            // The registry write can precede ISessionManager's logon notification.
                            foreach (var (target, watch) in _watches)
                                RefreshSession(target, watch.Key);
                        }
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        Logger.LogError(ex, "Could not update Duo instance session.");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Normal context shutdown.
            }
        }

        public void Dispose() => StopWatch();

        private static uint? ReadSessionId(RegistryKey key) => key.GetValue("SessionId") is int id && id >= 0 ? (uint)id : null;

        private void RefreshSession(DuoInstance instance, RegistryKey key)
        {
            if (ReadSessionId(key) is not uint id)
            {
                NotifySessionChanged(instance, null);
                return;
            }

            try
            {
                NotifySessionChanged(instance, SessionManager[id]);
            }
            catch (KeyNotFoundException)
            {
                // Wait for logon if Windows has not registered the session yet.
            }
        }

        private void NotifySessionChanged(DuoInstance instance, ISession? session)
        {
            var watch = _watches[instance];

            if (watch.Session == session)
                return;

            watch.Session = session;

            PublishSessionChange(instance, session);
        }

        private record Signal(DuoInstance? Instance = null, ISession? LoggedOff = null);

        private sealed class KeyWatch : IDisposable
        {
            internal RegistryKey Key { get; }
            internal ISession? Session { get; set; }

            readonly AutoResetEvent _signal = new(false);
            readonly RegisteredWaitHandle _wait;

            internal KeyWatch(RegistryKey key, Action changed)
            {
                Key = key;
                _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => changed(),
                    null, Timeout.Infinite, executeOnlyOnce: false);
            }

            internal void Arm()
            {
                const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
                const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

                int error = RegNotifyChangeKeyValue(Key.Handle, false,
                    REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC, _signal.SafeWaitHandle, true);

                if (error != 0)
                    throw new Win32Exception(error);
            }

            public void Dispose()
            {
                _wait.Unregister(null);
                Key.Dispose();
                _signal.Dispose();
            }

            [DllImport("advapi32.dll")]
            private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool subtree,
                uint filter, SafeWaitHandle signal, bool asynchronous);
        }
    }
}
