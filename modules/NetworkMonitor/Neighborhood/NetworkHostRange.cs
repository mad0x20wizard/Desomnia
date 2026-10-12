using MadWizard.Desomnia.Network.Neighborhood.Events;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkHostRange : NetworkResource, IIEnumerable<IPAddressRange>, IDisposable
    {
        readonly Lock _sync = new();

        readonly HashSet<NetworkHost> _hosts = [];
        readonly Dictionary<IPAddressRange, IPAddressRangeOptions> _ranges = [];

        public virtual IPAddressRangeOptions this[IPAddressRange range]
        {
            get { lock (_sync) return _ranges[range]; }
        }

        public event EventHandler<AddressRangeEventArgs>? AddressRangeAdded;
        public event EventHandler<AddressRangeEventArgs>? AddressRangeRemoved;

        internal bool IsDisposed { get { lock (_sync) return field; } set; }

        #region NetworkHost address synchronization
        public bool AddHostAddressRange(NetworkHost host)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);

                if (_hosts.Add(host))
                {
                    host.AddressAdded += Host_AddressAdded;
                    host.AddressRemoved += Host_AddressRemoved;

                    foreach (var ip in host.IPAddresses)
                    {
                        UpdateAddressRange(new(ip), default, changes);
                    }

                    return true;
                }
            }

            return false;
        }

        private void Host_AddressAdded(object? sender, AddressAddedEventArgs args)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                if (sender is NetworkHost host && _hosts.Contains(host))
                {
                    UpdateAddressRange(new(args.IPAddress), default, changes);
                }
            }
        }

        private void Host_AddressRemoved(object? sender, AddressRemovedEventArgs args)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                if (sender is NetworkHost host && _hosts.Contains(host))
                {
                    RemoveAllAddressRangesByIP(args.IPAddress, changes);
                }
            }
        }

        public bool RemoveHostAddressRange(NetworkHost host)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                if (_hosts.Remove(host))
                {
                    host.AddressRemoved -= Host_AddressRemoved;
                    host.AddressAdded -= Host_AddressAdded;

                    foreach (var ip in host.IPAddresses)
                    {
                        RemoveAllAddressRangesByIP(ip, changes);
                    }

                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Public AddressRange management
        public bool AddAddress(IPAddress ip, IPAddressRangeOptions options = default) => AddAddressRange(new(ip), options);

        public bool AddAddressRange(IPAddressRange range, IPAddressRangeOptions options = default)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);

                UpdateAddressRange(range, options, changes);
            }

            return changes.HasAdded;
        }

        public bool ShouldAddressRangeExpire(IPAddressRange range, out DateTime expires)
        {
            lock (_sync)
            {
                expires = DateTime.MaxValue;

                if (_ranges.TryGetValue(range, out var options) && options.Expires is DateTime date)
                {
                    expires = date;

                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// This is a high throughput method used for filtering
        /// and must perform without any allocations.
        /// </summary>
        public virtual bool Contains(IPAddress ip)
        {
            lock (_sync)
            {
                foreach (var (range, _) in _ranges)
                {
                    if (range.Contains(ip))
                        return true;
                }

                return false;
            }
        }

        public virtual bool RemoveAddress(IPAddress ip)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                RemoveAllAddressRangesByIP(ip, changes);
            }

            return changes.HasRemoved;
        }

        public bool RemoveAddressRange(IPAddressRange range, DateTime? expectedExpiration = null)
        {
            using ChangeSet changes = new(this);

            lock (_sync)
            {
                if (!IsDisposed && _ranges.TryGetValue(range, out var options))
                {
                    if (expectedExpiration is null || options.Expires == expectedExpiration)
                    {
                        _ranges.Remove(range);

                        changes[range] = false;
                    }
                }
            }

            return changes.HasRemoved;
        }
        #endregion

        #region Internal AddressRange management
        // Caller holds _sync. Notifications are raised only after releasing it.
        private bool UpdateAddressRange(IPAddressRange range, IPAddressRangeOptions options, ChangeSet changes)
        {
            if (options.HasFlags(IPAddressRangeFlags.Static))
                options.Expires = null;

            if (_ranges.TryGetValue(range, out var existing))
            {
                // static address ranges cannot change
                if (!existing.HasFlags(IPAddressRangeFlags.Static))
                {
                    bool extendExpires = existing.Expires is not null && (options.Expires is null || existing.Expires < options.Expires);

                    // A refresh may extend a lease, but never shorten a permanent or static entry.
                    if (options.HasFlags(IPAddressRangeFlags.Static) || extendExpires)
                    {
                        existing.Flags |= options.Flags; // make it static, but never remove it
                        existing.Expires = options.Expires;

                        _ranges[range] = existing;

                        changes[range] = null;
                    }
                }

                return false;
            }
            else
            {
                _ranges[range] = options;

                changes[range] = true;

                return true;
            }
        }

        // Caller holds _sync.
        private bool RemoveAllAddressRangesByIP(IPAddress ip, ChangeSet changes)
        {
            var ranges = _ranges.Keys.Where(range =>
                range.AddressCount == 1 &&
                range.Begin.Equals(ip) &&
                range.End.Equals(ip))
                .ToArray();

            foreach (var range in ranges)
            {
                if (_ranges.Remove(range))
                {
                    changes[range] = false;
                }
            }

            return ranges.Length > 0;
        }
        #endregion

        private void PublishChanges(ChangeSet changes, bool changed = false)
        {
            if (changes.HasChanges)
            {
                foreach (var (range, action) in changes)
                {
                    switch (action)
                    {
                        case true:
                            AddressRangeAdded?.Invoke(this, new(range));
                            break;

                        case false:
                            AddressRangeRemoved?.Invoke(this, new(range));
                            break;
                    }

                    changed = true;
                }
            }

            if (changed)
            {
                TriggerChanged();
            }
        }

        public virtual IEnumerator<IPAddressRange> GetEnumerator()
        {
            lock (_sync)
            {
                return ((IEnumerable<IPAddressRange>)[.. _ranges.Keys]).GetEnumerator();
            }
        }

        void IDisposable.Dispose()
        {
            lock (_sync) if (!IsDisposed)
            {
                IsDisposed = true;

                // remove all host and address ranges
                foreach (var host in _hosts.ToArray())
                    RemoveHostAddressRange(host);
                foreach (var range in _ranges.Keys.ToArray())
                    RemoveAddressRange(range);

                PublishChanges(default, true);
            }
        }

        private readonly struct ChangeSet(NetworkHostRange? range = null) : IIEnumerable<KeyValuePair<IPAddressRange, bool?>>, IDisposable
        {
            private readonly Dictionary<IPAddressRange, bool?> _ranges = [];

            internal bool HasChanges => _ranges?.Count > 0;

            internal bool HasAdded => HasChanges && _ranges.Values.Any(b => b == true);
            internal bool HasRemoved => HasChanges && _ranges.Values.Any(b => b == false);

            internal bool? this[IPAddressRange range]
            {
                get => _ranges[range];
                set => _ranges[range] = value;
            }

            public IEnumerator<KeyValuePair<IPAddressRange, bool?>> GetEnumerator()
            {
                return _ranges.GetEnumerator();
            }

            void IDisposable.Dispose()
            {
                if (HasChanges)
                {
                    range?.PublishChanges(this);

                    _ranges.Clear();
                }
            }
        }
    }
}
