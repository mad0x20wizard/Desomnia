using ConcurrentCollections;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using SharpPcap.LibPcap;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Network.Device.Filter
{
    #region Traffic Types
    public interface ITrafficType;

    public readonly record struct ARPTrafficType : ITrafficType;
    public readonly record struct NDPTrafficType : ITrafficType;
    public readonly record struct WOLTrafficType : ITrafficType;

    public readonly record struct IPv4TrafficType : ITrafficType;
    public readonly record struct IPv6TrafficType : ITrafficType;

    public readonly record struct TCPTrafficType(ushort? Port, bool WithData = false) : ITrafficType;
    public readonly record struct UDPTrafficType(ushort? Port) : ITrafficType;

    public readonly record struct ICMPEchoTrafficType : ITrafficType;
    #endregion

    public class BerkeleyPacketFilter : INetworkService, IDisposable
    {
        public required ILogger<BerkeleyPacketFilter> Logger { private get; init; }

        public required NetworkDevice Device
        {
            get; init
            {
                value.CaptureStarting   += Device_CaptureStarting;
                value.CaptureStopped    += Device_CaptureStopped;

                field = value;
            }
        }

        public required BPFOptions          Options             { private get; init; }
        public required ReassemblyOptions   ReassemblyOptions   { private get; init; }

        /// <summary>Lazy to break the construction cycle; queried for per-host block-by-default state.</summary>
        public required Lazy<NetworkMonitor>? Monitor { private get; init; }

        readonly ConcurrentHashSet<TrafficFilterRequest> _requests = [];

        private bool _shouldUpdateFilter = false;
        private string? _validatedFilter;

        private void Device_CaptureStarting(object? sender, EventArgs args)
        {
            _validatedFilter = null;
            UpdateFilter();
            _shouldUpdateFilter = true;
        }

        private void Device_CaptureStopped(object? sender, EventArgs args)
        {
            _shouldUpdateFilter = false;
            _validatedFilter = null;
        }

        public void Dispose()
        {
            Device.CaptureStarting -= Device_CaptureStarting;
            Device.CaptureStopped -= Device_CaptureStopped;
            _shouldUpdateFilter = false;
        }

        internal void AddRequest(TrafficFilterRequest request)
        {
            if (_requests.Add(request))
            {
                request.Disposed += (sender, args) =>
                {
                    if (_requests.TryRemove(request))
                    {
                        if (_shouldUpdateFilter) UpdateFilter();
                    }
                };

                if (_shouldUpdateFilter) UpdateFilter();
            }
        }

        /// <summary>
        /// Builds a positive whitelist of exactly the traffic the registered <see cref="TrafficFilterRequest"/>s
        /// asked for and pushes it down into the kernel via the BPF engine.
        /// <para>
        /// Requests are grouped into per-host tables (see <see cref="Aggregate"/>): a host whose table holds
        /// only address-family types wakes unconditionally and widens its families to the full demand baseline,
        /// while port-based types make the capture for that host precise. Global requests (mDNS, DHCP, knocking, ...)
        /// contribute their shapes independently.
        /// </para>
        /// <para>
        /// A compiled BPF program has a finite instruction budget, so a very port-rich whitelist can be
        /// rejected by the kernel even when libpcap accepts it. We compile each candidate before
        /// installation and enforce the supplied budget. The first candidate that <see cref="TrySetFilter"/>
        /// accepts wins. Each rung drops one refinement (TCP ports, then UDP ports, then protocol
        /// sub-typing), retaining the requested address families and protocols.
        /// </para>
        /// </summary>
        private void UpdateFilter()
        {
            if (Options.Expression is string expression)
            {
                // Explicit expressions are never simplified or replaced on failure.
                SetFilter(expression);
                return;
            }

            string? lastTried = null;

            foreach (var filter in BuildFilterCandidates(Aggregate()))
            {
                // Skip rungs that collapsed to the same expression we already rejected.
                if (filter == lastTried)
                    continue;

                if (TrySetFilter(filter))
                {
                    return;
                }

                lastTried = filter;
            }

            Logger.LogWarning("No BPF filter could be applied; capturing without a kernel filter.");

            SetFilter("");
        }

        /// <summary>
        /// Re-evaluates the capture filter. Call after runtime changes that affect a host's
        /// block-by-default state without adding or removing a <see cref="TrafficFilterRequest"/>
        /// (e.g. a dynamically created service watch being tracked).
        /// </summary>
        internal void Refresh()
        {
            if (_shouldUpdateFilter) UpdateFilter();
        }

        async Task INetworkService.Shutdown(NetworkShutdownReason reason)
        {
            if (_shouldUpdateFilter && Device.Device.Filter != "") TrySetFilter("");

            _shouldUpdateFilter = false;
        }

        private bool TrySetFilter(string filter)
        {
            try
            {
                SetFilter(filter);

                return true;
            }
            catch (Exception ex)
            {
                // A rejected filter is expected while walking the precision ladder down; only the
                // final give-up in UpdateFilter is worth a louder log.
                Logger.LogDebug(ex, "BPF rejected; trying to simplify.");

                return false;
            }
        }

        private void SetFilter(string filter)
        {
            var device = Device.Device;

            if (device.Filter == filter && _validatedFilter == filter)
                return;

            BpfProgram CompileFilter(string expression)
            {
                return device is PcapDevice pcap
                    ? BpfProgram.Create(pcap.Handle, expression)
                    : BpfProgram.Create(device.LinkType, expression);
            }

            // Compilation alone does not prove that the kernel will accept a program. In
            // particular, Linux libpcap can silently fall back to filtering in user space.
            // Match SharpPcap's Filter setter: compile on the opened handle with optimization enabled.
            using (var program = CompileFilter(filter))
            {
                // The native bpf_program begins with the unsigned 32-bit bf_len field.
                uint count = unchecked((uint)Marshal.ReadInt32(program.DangerousGetHandle()));
                Logger.LogDebug("BPF [{Instructions}/{Limit}] -> '{Filter}'",
                    count, Options.InstructionLimit?.ToString() ?? "?", filter);

                if (Options.InstructionLimit is int limit && count > limit)
                    throw new InvalidOperationException($"BPF requires {count} instructions, exceeding the limit of {limit}.");
            }

            device.Filter = filter;
            _validatedFilter = filter;
        }

        #region Traffic demand aggregation
        private sealed record TrafficShape(IPTrafficShape IPv4, IPTrafficShape IPv6, bool ARP, bool NDP, bool WoL,
            ReassemblyProtocol Fragments);

        /// <summary>
        /// The capture requirements of one address family (IPv4 / IPv6). "Any" flags absorb port lists,
        /// so joining an unconditional host with port-precise ones stays correct.
        /// </summary>
        private sealed class IPTrafficShape
        {
            public bool                         TCPAnySyn;
            public bool                         TCPAnyPayload;
            public readonly SortedSet<ushort>   TCPSynPorts = [];
            public readonly SortedSet<ushort>   TCPPayloadPorts = [];

            public bool                         UDPAny;
            public readonly SortedSet<ushort>   UDPPorts = [];

            public bool                         ICMPEcho;

            public bool IsEmpty => !TCPAnySyn && !TCPAnyPayload && TCPSynPorts.Count == 0 && TCPPayloadPorts.Count == 0
                                && !UDPAny && UDPPorts.Count == 0
                                && !ICMPEcho;

            public void Add(TCPTrafficType tcp)
            {
                if (tcp.Port is ushort port)
                    (tcp.WithData ? TCPPayloadPorts : TCPSynPorts).Add(port);
                else if (tcp.WithData)
                    TCPAnyPayload = true;
                else
                    TCPAnySyn = true;
            }

            public void Add(UDPTrafficType udp)
            {
                if (udp.Port is ushort port)
                    UDPPorts.Add(port);
                else
                    UDPAny = true;
            }

            /// <summary>Normalizes the cell: wider needs absorb the narrower ones they already cover.</summary>
            public void Absorb()
            {
                if (TCPAnyPayload)
                {
                    TCPAnySyn = false;
                    TCPPayloadPorts.Clear();
                }

                if (TCPAnyPayload || TCPAnySyn)
                    TCPSynPorts.Clear();

                TCPSynPorts.ExceptWith(TCPPayloadPorts); // full stream capture includes the SYN

                if (UDPAny)
                    UDPPorts.Clear();
            }

            /// <summary>The full demand baseline; mirrors the traffic DemandByIP reacts to.</summary>
            public void MakeUnconditional()
            {
                TCPAnySyn = true;
                UDPAny = true;
                ICMPEcho = true;
            }

            // -- ladder steps; each one only ever widens, so wanted traffic is never lost --

            public void MakeTCPPortsUnconditional()
            {
                if (TCPSynPorts.Count > 0) TCPAnySyn = true;
                if (TCPPayloadPorts.Count > 0) TCPAnyPayload = true;

                Absorb();
            }

            public void MakeUDPPortsUnconditional()
            {
                if (UDPPorts.Count > 0) UDPAny = true;

                Absorb();
            }

            public void MakeTCPUnconditional()
            {
                if (TCPAnySyn) TCPAnyPayload = true;

                TCPPayloadPorts.UnionWith(TCPSynPorts);
                TCPSynPorts.Clear();

                Absorb();
            }

            /// <param name="family">which address family the cell describes; null for a family-agnostic clause</param>
            /// <param name="preciseICMP">whether to match ICMP echo by exact type or by bare protocol</param>
            internal string ToBPFExpression(AddressFamily? family, bool preciseICMP)
            {
                var parts = new List<string>();

                // The symbolic tcp[tcpflags] only resolves for IPv4; over IPv6 the flags must be read by raw
                // offset (TCP follows the fixed 40-byte IPv6 header, so the flag byte is ip6[53]).
                string syn = family switch
                {
                    AddressFamily.InterNetwork      => "tcp[tcpflags] & tcp-syn != 0",
                    AddressFamily.InterNetworkV6    => "ip6[53] & 0x02 != 0",

                    _ => "(ip and (tcp[tcpflags] & tcp-syn != 0)) or (ip6 and (ip6[53] & 0x02 != 0))",
                };

                if (this.TCPAnyPayload)
                {
                    parts.Add("tcp");
                }
                else
                {
                    // Only the SYN of a connection attempt is needed, which strips out the whole payload stream.
                    if (this.TCPAnySyn)
                        parts.Add($"(tcp and ({syn}))");
                    else if (this.TCPSynPorts.Count > 0)
                        parts.Add($"({PortMatch("tcp", this.TCPSynPorts)} and ({syn}))");

                    if (this.TCPPayloadPorts.Count > 0)
                        parts.Add(PortMatch("tcp", this.TCPPayloadPorts));
                }

                if (this.UDPAny)
                    parts.Add("udp");
                else if (this.UDPPorts.Count > 0)
                    parts.Add(PortMatch("udp", this.UDPPorts));

                if (this.ICMPEcho)
                {
                    if (family != AddressFamily.InterNetworkV6)
                        parts.Add(preciseICMP ? "(icmp and (icmp[icmptype] = icmp-echo or icmp[icmptype] = icmp-echoreply))" : "icmp");

                    if (family != AddressFamily.InterNetwork)
                        parts.Add(preciseICMP ? "(icmp6 and (ip6[40] = 128 or ip6[40] = 129))" : "icmp6"); // ICMPv6 echo request/reply
                }

                return string.Join(" or ", parts);
            }

            /// <summary>
            /// A bounded protocol allowance, independent of the number of service ports.
            /// Port, flag and ICMP type checks belong to the application after reassembly.
            /// </summary>
            internal string ToFragmentBPFExpression(bool ipv6)
            {
                string protocol = ipv6 ? "ip6[40]" : "ip[9]";
                var parts = new List<string>();
                if (TCPAnySyn || TCPAnyPayload || TCPSynPorts.Count > 0 || TCPPayloadPorts.Count > 0)
                    parts.Add($"{protocol} = 6");
                if (UDPAny || UDPPorts.Count > 0)
                    parts.Add($"{protocol} = 17");
                if (ICMPEcho) parts.Add($"{protocol} = {(ipv6 ? 58 : 1)}");

                string known = string.Join(" or ", parts);
                if (!ipv6)
                    return $"(ip and (ip[6:2] & 0x3fff != 0) and ({known}))";

                // RFC 8200 permits later fragments to have a different Next Header value. Only
                // the first fragment determines the reassembled protocol, so later ones remain
                // undecidable even when their own Fragment header names a different protocol.
                // Extension chains are deliberately admitted without walking them. protochain
                // disables BPF optimization and fails with variable link-layer offsets (e.g.
                // Linux VLAN offload). This also admits unfragmented extension chains, scoped
                // to families with IP demand; the application performs the remaining checks.
                string Extension(string field) => $"({field} = 0 or {field} = 43 or {field} = 60 or {field} = 51)";
                return $"(ip6 and ({Extension("ip6[6]")} or (ip6[6] = 44 and "
                    + $"(ip6[42:2] & 0xfff8 != 0 or {Extension(protocol)} or ({known})))))";
            }
        }

        /// <summary>
        /// Flattens all requests into per-family capture requirements.
        /// <para>
        /// Host-scoped requests form one table per host: no address-family types means the host is a
        /// dead end (it cannot see demand, so it contributes nothing); family types without any
        /// port-based (TCP/UDP) types mean the host wakes unconditionally and needs the full demand
        /// baseline for its families; port-based types make the host's capture precise.
        /// Global requests contribute their shapes as-is, family types acting as narrowing qualifiers.
        /// </para>
        /// </summary>
        private TrafficShape Aggregate()
        {
            IPTrafficShape ip4 = new(), ip6 = new();

            bool arp = false, wol = false, ndp = false;

            foreach (var group in _requests.GroupBy(request => request.Host))
            {
                bool captureUnconditional;

                if (group.Key is not null)
                {
                    if (Monitor?.Value[group.Key] is not HostDemandWatch watch)
                        continue;

                    // A placeholder host awaits the registration of its services (via the Sleep Proxy
                    // endpoint); until then nothing shall wake it, so it is a dead end for capturing.
                    if (watch.FilterOptions.AwaitServices)
                        continue;

                    captureUnconditional = watch is not LocalHostWatch;
                }
                else
                {
                    captureUnconditional = false;
                }

                // A host's requests form one table; global requests each stand on their own.
                IEnumerable<ITrafficType[]> tables = group.Key is null
                    ? group.Select(request => request.Types)
                    : [[.. group.SelectMany(request => request.Types).Distinct()]];

                foreach (var types in tables)
                {
                    arp |= types.OfType<ARPTrafficType>().Any();
                    wol |= types.OfType<WOLTrafficType>().Any();
                    ndp |= types.OfType<NDPTrafficType>().Any();

                    bool hasV4 = types.OfType<IPv4TrafficType>().Any();
                    bool hasV6 = types.OfType<IPv6TrafficType>().Any();

                    if (group.Key is not null && !hasV4 && !hasV6)
                        continue; // dead end: a host without addresses cannot see demand

                    IPTrafficShape[] cells = (hasV4, hasV6) switch
                    {
                        (true, true) => [ip4, ip6],
                        (true, false) => [ip4],
                        (false, true) => [ip6],

                        _ => [ip4, ip6], // an unqualified global request applies to both families
                    };

                    var tcp     = types.OfType<TCPTrafficType>().ToList();
                    var udp     = types.OfType<UDPTrafficType>().ToList();
                    var echo    = types.OfType<ICMPEchoTrafficType>().Any();

                    bool unconditional = captureUnconditional && (tcp.Count == 0 && udp.Count == 0);

                    foreach (var cell in cells)
                    {
                        if (unconditional)
                        {
                            cell.MakeUnconditional();
                        }
                        else
                        {
                            foreach (var type in tcp)
                                cell.Add(type);
                            foreach (var type in udp)
                                cell.Add(type);
                        }

                        cell.ICMPEcho |= echo;
                    }
                }
            }

            ip4.Absorb();
            ip6.Absorb();

            return new(ip4, ip6, arp, ndp, wol, ReassemblyOptions.Protocol);
        }
        #endregion

        #region Filter construction
        private static IEnumerable<string> BuildFilterCandidates(TrafficShape shape)
        {
            yield return BuildFilter(shape, preciseICMP: true);

            // remove TCP port requirements
            shape.IPv4.MakeTCPPortsUnconditional();
            shape.IPv6.MakeTCPPortsUnconditional();
            yield return BuildFilter(shape, preciseICMP: true);

            // remove UDP port requirements
            shape.IPv4.MakeUDPPortsUnconditional();
            shape.IPv6.MakeUDPPortsUnconditional();
            yield return BuildFilter(shape, preciseICMP: true);

            // remove TCP SYN requirement
            shape.IPv4.MakeTCPUnconditional();
            shape.IPv6.MakeTCPUnconditional();
            yield return BuildFilter(shape, preciseICMP: true);

            // ignore ICMP type
            yield return BuildFilter(shape, preciseICMP: false);
        }

        private static string BuildFilter(TrafficShape demand, bool preciseICMP)
        {
            var clauses = new List<string>();

            if (demand.ARP) clauses.Add("arp");
            // NDP cannot be fragmented. A bare icmp6 predicate can also match a Fragment header.
            if (demand.NDP) clauses.Add(preciseICMP ? "(ip6 and ip6[6] = 58 and ip6[40] >= 133 and ip6[40] <= 137)" : "(ip6 and ip6[6] = 58)");
            if (demand.WoL) clauses.Add("ether proto 0x0842"); // EthernetType.WakeOnLan (layer-2 magic packet)

            // Keep ordinary traffic on the inexpensive path. Fragments must go through the
            // family-gated predicates below, even when a bare tcp/udp predicate would match them.
            if (!demand.IPv4.IsEmpty)
                clauses.Add($"(ip and (ip[6:2] & 0x3fff = 0) and ({demand.IPv4.ToBPFExpression(AddressFamily.InterNetwork, preciseICMP)}))");
            if (!demand.IPv6.IsEmpty)
                clauses.Add($"(ip6 and ip6[6] != 44 and ({demand.IPv6.ToBPFExpression(AddressFamily.InterNetworkV6, preciseICMP)}))");

            var fragments = new List<string>();
            if (!demand.IPv4.IsEmpty && demand.Fragments.HasFlag(ReassemblyProtocol.IPv4))
                fragments.Add(demand.IPv4.ToFragmentBPFExpression(ipv6: false));
            if (!demand.IPv6.IsEmpty && demand.Fragments.HasFlag(ReassemblyProtocol.IPv6))
                fragments.Add(demand.IPv6.ToFragmentBPFExpression(ipv6: true));
            if (fragments.Count > 0)
            {
                string fragment = "(" + string.Join(" or ", fragments) + ")";
                clauses.Add(fragment);
                // vlan changes offsets for everything following it in the expression. Nested
                // branches must therefore come last, ordered from zero to four tags, matching
                // IPFragment.ReadEthernet. Apply the same predicates inside every encapsulation.
                string tagged = fragment;
                for (int depth = 1; depth < 4; depth++)
                    tagged = $"({fragment} or (vlan and {tagged}))";
                clauses.Add($"(vlan and {tagged})");
            }

            // No Ethernet frame has length zero. Unlike an empty expression, this does not
            // accidentally enable unrestricted capture when there are no registrations.
            return clauses.Count == 0 ? "len = 0" : string.Join(" or ", clauses);
        }

        private static string PortMatch(string protocol, IEnumerable<ushort> ports) => "(" + string.Join(" or ", ports.Select(port => $"{protocol} port {port}")) + ")";
        #endregion
    }

    public class TrafficFilterRequest : IDisposable
    {
        public ITrafficType[] Types { get; private init; }

        /// <summary>The host whose watch needs this traffic, or null for device-global capture needs.</summary>
        public NetworkHost? Host    { get; }

        public event EventHandler? Disposed;

        public TrafficFilterRequest(ITrafficType[] types, BerkeleyPacketFilter? filter = null, NetworkHost? host = null)
        {
            Types   = types;

            Host    = host;

            filter?.AddRequest(this);
        }

        public void Dispose()
        {
            Disposed?.Invoke(this, EventArgs.Empty);
            Disposed = null;
        }
    }
}
