using MadWizard.Desomnia.Network.Configuration.Options;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Capture;

internal abstract class IPReassembly : IFragmentReassembly, IDisposable
{
    private const int MaximumDatagrams = 1024;
    private const long MaximumRetainedBytes = 16 * 1024 * 1024;
    private const int MaximumFragments = 8192;
    private readonly bool _enabled, _passthrough;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<IPFragment.FragmentKey, Pending> _pending = [];
    private ITimer? _timer;
    private long _retainedBytes;
    private bool _disposed;

    protected IPReassembly(IPReassemblyFamily family, IPReassemblyOptions options, TimeProvider? clock = null)
    {
        _enabled = (options.Family & family) != 0;
        _passthrough = (options.Family & IPReassemblyFamily.Passthrough) != 0;
        _timeout = options.Timeout;
        _clock = clock ?? TimeProvider.System;
    }

    protected abstract bool TryReadFragment(EthernetPacket packet, out IPFragment? fragment);

    public bool TryReassemble(EthernetPacket packet, out INetworkCapture? capture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        capture = null;
        if (!TryReadFragment(packet, out var current)) return false;
        if (current is null || !_enabled) return true;
        if (_passthrough)
        {
            capture = new EthernetCapture(packet);
            return true;
        }
        if (current.IsAtomic)
        {
            if (current.IsValid) capture = new FragmentedIPCapture([current]);
            return true; // Atomic fragments never join or invalidate pending groups.
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Expire();
            if (!_pending.TryGetValue(current.Key, out var pending))
            {
                if (_pending.Count >= MaximumDatagrams) return true;
                _pending.Add(current.Key, pending = new(_clock.GetTimestamp()));
                _timer ??= _clock.CreateTimer(_ => Sweep(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            if (pending.Rejected) return true;
            if (!current.IsValid) return Reject(pending);
            pending.ECN |= 1 << current.ECN;
            if ((pending.ECN & 1) != 0 && pending.ECN != 1)
                return Reject(pending); // Do not mix Not-ECT with ECN-capable fragments.
            int end = current.Offset + current.Length;
            if (pending.Fragments.TryGetValue(current.Offset, out var duplicate)
                && duplicate.More == current.More && duplicate.Protocol == current.Protocol
                && duplicate.ECN == current.ECN && duplicate.Data.SequenceEqual(current.Data))
                return true;
            bool overlaps = false;
            foreach (var part in pending.Fragments.Values)
            {
                if ((part.Offset < end && current.Offset < part.Offset + part.Length)
                    || (!current.More && (part.Offset + part.Length > end || (part.More && part.Offset + part.Length >= end))))
                {
                    overlaps = true;
                    break;
                }
            }
            if (pending.Fragments.Count >= MaximumFragments || _retainedBytes + current.WireLength > MaximumRetainedBytes
                || (pending.Length is int known && (end > known || (current.More && end >= known) || (!current.More && end != known)))
                || overlaps)
                return Reject(pending);
            pending.Fragments.Add(current.Offset, current);
            pending.Bytes += current.WireLength;
            _retainedBytes += current.WireLength;
            if (!current.More) pending.Length = end;
            if (pending.Length is not int length || !pending.Fragments.TryGetValue(0, out var first))
                return true;
            if (length > first.MaximumPayloadLength)
                return Reject(pending);
            int covered = 0;
            foreach (var part in pending.Fragments.Values)
            {
                if (part.Offset != covered) return true;
                covered += part.Length;
            }
            if (covered != length) return true;
            var fragments = pending.Fragments.Values.ToArray();
            _retainedBytes -= pending.Bytes;
            _pending.Remove(current.Key);
            capture = new FragmentedIPCapture(fragments);
            return true;
        }
    }

    private bool Reject(Pending pending)
    {
        _retainedBytes -= pending.Bytes;
        pending.Bytes = 0;
        pending.Fragments.Clear();
        pending.Rejected = true; // Keep a bounded tombstone until the original deadline.
        return true;
    }

    private void Sweep() { lock (_gate) { if (!_disposed) Expire(); } }
    private void Expire()
    {
        long now = _clock.GetTimestamp();
        foreach (var entry in _pending.Where(p => _clock.GetElapsedTime(p.Value.Started, now) >= _timeout).ToArray())
        {
            _retainedBytes -= entry.Value.Bytes;
            _pending.Remove(entry.Key);
        }
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _timer?.Dispose(); _pending.Clear(); _retainedBytes = 0; }
    }
    private sealed class Pending(long started)
    {
        public readonly long Started = started;
        public readonly SortedList<int, IPFragment> Fragments = [];
        public int? Length;
        public long Bytes;
        public bool Rejected;
        public int ECN;
    }
}
