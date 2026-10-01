using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class KeyWatch : IDisposable
    {
        public RegistryKey Key { get; }

        readonly Lock _lock = new();
        RegistryKey? _notificationKey;
        AutoResetEvent? _signal;
        RegisteredWaitHandle? _wait;
        EventHandler? _changed;
        bool _disposed;

        internal KeyWatch(RegistryKey key)
        {
            Key = key;
        }

        internal event EventHandler Changed
        {
            add
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (value is null) return;

                    if (_changed is null)
                    {
                        try
                        {
                            // Closing this separate handle cancels notifications without closing Key.
                            _notificationKey = Key.OpenSubKey(string.Empty)
                                ?? throw new FileNotFoundException($"Registry key not found: {Key.Name}");
                            _signal = new(false);
                            _wait = ThreadPool.RegisterWaitForSingleObject(_signal, OnChanged,
                                _signal, Timeout.Infinite, executeOnlyOnce: false);

                            RegisterChange();
                        }
                        catch
                        {
                            Disarm();
                            throw;
                        }
                    }

                    _changed += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _changed -= value;

                    if (_changed is null)
                        Disarm();
                }
            }
        }

        private void OnChanged(object? state, bool timedOut)
        {
            lock (_lock)
            {
                // Ignore callbacks queued before the last listener was removed.
                if (state != _signal || _changed is null)
                    return;

                RegisterChange();
                _changed.Invoke(this, EventArgs.Empty);
            }
        }

        private void RegisterChange()
        {
            const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
            const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

            int error = RegNotifyChangeKeyValue(_notificationKey!.Handle, false,
                REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC, _signal!.SafeWaitHandle, true);

            if (error != 0)
                throw new Win32Exception(error);
        }

        private void Disarm()
        {
            _wait?.Unregister(null);
            _notificationKey?.Dispose();
            _signal?.Dispose();
            _wait = null;
            _notificationKey = null;
            _signal = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _changed = null;
                Disarm();
                Key.Dispose();
            }
        }

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, 
            bool subtree, uint filter, SafeWaitHandle signal, bool asynchronous);
    }
}
