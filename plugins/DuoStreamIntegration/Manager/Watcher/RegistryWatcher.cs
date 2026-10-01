using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class RegistryWatcher : BaseWatcher, IDisposable
    {
        readonly Dictionary<DuoInstance, InstanceKeyWatch> _watches = [];

        // current watch session
        CancellationTokenSource? _lifetime;
        Channel<Signal>? _channel;
        Task? _watchTask;

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
                var channel = _channel;
                foreach (var instance in instances)
                {
                    token.ThrowIfCancellationRequested();

                    // create Registry watch
                    var watch = new InstanceKeyWatch(instance);
                    watch.Changed += (_, _) => channel.Writer.TryWrite(new(instance));
                    _watches.Add(instance, watch);

                    RefreshWatch(watch);
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

        private void SessionManager_UserLogon(object? sender, ISession session) => _channel?.Writer.TryWrite(new());
        private void SessionManager_UserLogoff(object? sender, ISession session) => _channel?.Writer.TryWrite(new(LoggedOff: session));

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
                                    if (watch.SessionId == session.Id)
                                        watch.SessionId = null;
                                }
                                finally
                                {
                                    NotifySessionChange(instance, null);
                                }
                            }
                        }
                        else if (signal.Instance is DuoInstance instance)
                        {
                            RefreshWatch(_watches[instance]);
                        }
                        else foreach (var watch in _watches.Values)
                        {
                            RefreshWatch(watch); // FALLBACK: The registry write can precede ISessionManager's logon notification.
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

        private void RefreshWatch(InstanceKeyWatch watch)
        {
            try
            {
                NotifySessionChange(watch.Instance, watch.SessionId is uint id ? SessionManager[id] : null);
            }
            catch (KeyNotFoundException)
            {
                // Wait for logon if Windows has not registered the session yet.
            }
        }

        protected override void NotifySessionChange(DuoInstance instance, ISession? session)
        {
            var watch = _watches[instance];

            if (watch.Session != session) // only notify on actual changes
            {
                base.NotifySessionChange(instance, watch.Session = session);
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

                _watchTask?.GetAwaiter().GetResult(); // wait until finish
            }
            finally
            {
                // end all Registry watches
                foreach (var watch in _watches.Values)
                    watch.Dispose();
                _watches.Clear();

                // close the channel
                _channel?.Writer.TryComplete();
                _channel = null;

                _watchTask = null;

                _lifetime.Dispose();
                _lifetime = null;
            }
        }

        void IDisposable.Dispose()
        {
            StopWatch();
        }

        private record Signal(DuoInstance? Instance = null, ISession? LoggedOff = null);

        private class InstanceKeyWatch : KeyWatch
        {
            private static RegistryKey OpenInstanceKey(DuoInstance instance) =>
                Registry.LocalMachine.OpenSubKey(DuoService.REG_DuoInstances + '\\' + instance.Name, writable: true)
                    ?? throw new FileNotFoundException($"Duo instance key not found: {instance.Name}");

            internal InstanceKeyWatch(DuoInstance instance) : base(OpenInstanceKey(instance))
            {
                Instance = instance;
            }

            internal DuoInstance Instance { get; private init; }

            internal ISession? Session { get; set; }

            internal uint? SessionId
            {
                get => Key.GetValue("SessionId") is int id && id >= 0 ? (uint)id : null;

                set
                {
                    if (value != null)
                    {
                        Key.SetValue("SessionId", value, RegistryValueKind.DWord);
                    }
                    else
                    {
                        Key.DeleteValue("SessionId", throwOnMissingValue: false);
                    }
                }
            }
        }
    }
}
