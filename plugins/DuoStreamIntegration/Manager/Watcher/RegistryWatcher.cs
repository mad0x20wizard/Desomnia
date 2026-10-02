using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class RegistryWatcher : IDuoWatcher
    {
        const bool FixDuoSessionIdOnLogoff = true;

        public required ILogger<RegistryWatcher> Logger { private get; init; }

        public required SessionMonitor SessionMonitor { private get; init; }

        private ISession? ValidateSession(DuoInstance instance, uint? sid)
        {
            if (sid is not null)
            {
                if (SessionMonitor.TakeSnapshot().FirstOrDefault(w => w.Session.Id == sid) is SessionWatch watch && watch.Session is ISession session)
                {
                    if (!string.Equals(session.UserName, instance.Settings.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Session with id = {sid} is invalid: '{session.UserName}' != '{instance.Settings.UserName}'");
                    }

                    lock (instance) if (instance.Session is null)
                    {
                        instance.StartTracking(watch); // late binding
                    }

                    return session;
                }
                else
                {
                    throw new KeyNotFoundException($"Session with id = {sid} was not found");
                }
            }
            else
            {
                return null;
            }
        }

        IEnumerable<InstanceKeyWatch> WatchInstances(IEnumerable<DuoInstance> instances, EventHandler handler)
        {
            foreach (var instance in instances)
            {
                var watch = new InstanceKeyWatch(instance);

                try
                {
                    watch.Session = ValidateSession(instance, watch.SessionId);
                }
                catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
                {
                    Logger.LogWarning(ex, "State of instance '{Name}' is invalid; removing SessionId", instance.Name);

                    watch.SessionId = null; // clear stale session id
                }

                watch.Changed += handler;

                yield return watch;
            }
        }

        async IAsyncEnumerable<WatchSignal> IDuoWatcher.WatchAsync(IEnumerable<DuoInstance> instances, [EnumeratorCancellation] CancellationToken token)
        {
            using LocalChannel<WatchSignal> channel = Channel.CreateUnbounded<WatchSignal>(new() { SingleReader = true });

            void ChangeHandler(object? sender, EventArgs args)
            {
                try
                {
                    if (sender is InstanceKeyWatch key
                        && key.Instance is DuoInstance instance
                        && key.SessionId != key.Session?.Id)
                    {
                        key.Session = ValidateSession(instance, key.SessionId);

                        /**
                         * Duo updates the SessionId only after the instance is running.
                         * Removing the SessionId does not necesserily coincide
                         * with stopping the instance, which is why we ignore it here.
                         */
                        if (key.Session is not null)
                        {
                            channel.Writer.TryWrite(new(instance, true));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not handle registry key update");
                }
            }

            await SessionMonitor.StartupFinished.WaitAsync(token);

            using (new CompositeDisposable(WatchInstances(instances, ChangeHandler)))
            {
                await foreach (var signal in channel.Reader.ReadAllAsync(token))
                {
                    yield return signal;
                }
            }
        }

        private class InstanceKeyWatch : RegistryKeyWatch
        {
            private static RegistryKey OpenInstanceKey(DuoInstance instance) =>
                Registry.LocalMachine.OpenSubKey(DuoService.REG_DuoInstances + '\\' + instance.Name, writable: true)
                    ?? throw new FileNotFoundException($"Duo instance key not found: {instance.Name}");

            internal InstanceKeyWatch(DuoInstance instance) : base(OpenInstanceKey(instance))
            {
                Instance = instance;
            }

            internal DuoInstance Instance { get; private init; }

            internal ISession? Session
            {
                get; set
                {
                    if (FixDuoSessionIdOnLogoff)
                    {
                        field?.LoggedOff -= Session_LoggedOff;
                        value?.LoggedOff += Session_LoggedOff;
                    }

                    field = value;
                }
            }

            private void Session_LoggedOff(object? sender, EventArgs args)
            {
                if (SessionId == (sender as ISession)?.Id)
                {
                    SessionId = null;
                }
            }

            internal uint? SessionId
            {
                get => Key["SessionId"] is int id && id >= 0 ? (uint)id : null;

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

            public override void Dispose()
            {
                Session = null;

                base.Dispose();
            }
        }
    }
}
