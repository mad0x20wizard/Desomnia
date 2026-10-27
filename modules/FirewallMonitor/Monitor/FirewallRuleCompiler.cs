using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Firewall.Manager;
using NetTools;
using System.Net;
using System.Numerics;

namespace MadWizard.Desomnia.Firewall
{
    /// <summary>
    /// Projects static source predicates into broad permits and effective exclusions.
    /// Nested exclusions become blocks only where no alternative Must authorizes the source.
    /// </summary>
    internal static class FirewallRuleCompiler
    {
        internal static FirewallRule[] Compile(IEnumerable<IPAddress> localAddresses, IPService service,
            IEnumerable<PacketFilterRule> rules)
        {
            var configured = rules.ToArray();
            if (service.Protocol == (IPProtocol.TCP | IPProtocol.UDP))
            {
                var addresses = localAddresses.ToArray();
                return [.. Compile(addresses, new(IPProtocol.TCP, service.Port), configured),
                    .. Compile(addresses, new(IPProtocol.UDP, service.Port), configured)];
            }
            if (service.Protocol is not (IPProtocol.TCP or IPProtocol.UDP))
                throw new NotSupportedException($"Unsupported service protocol {service.Protocol}.");

            List<SourceRule> applicable = [];
            foreach (var rule in configured)
            {
                // Validate even rules for other ports, so unsupported dependencies are not silently ignored.
                var source = CompileSource(rule);
                if (rule is not ServiceFilterRule transport
                    || transport.Service.Port == service.Port && transport.Service.Protocol.HasFlag(service.Protocol))
                    applicable.Add(source);
            }
            var permits = applicable.Where(r => r.Type == FilterRuleType.Must).ToArray();
            var denies = applicable.Where(r => r.Type == FilterRuleType.MustNot).ToArray();

            List<FirewallRule> result = [];
            foreach (var local in localAddresses.Distinct())
            {
                int bytes = local.GetAddressBytes().Length;
                BigInteger end = BigInteger.One << (bytes * 8);
                var any = new IPAddressRange(Address(BigInteger.Zero, bytes), Address(end - 1, bytes));

                // PacketRuleFilter requires a Must match if any Must exists in the group,
                // even when the Must describes another service.
                if (configured.Any(r => r.Type == FilterRuleType.Must))
                    result.Add(new(local, service, any, FirewallAction.Block, FirewallPriority.Default));

                SortedSet<BigInteger> boundaries = [BigInteger.Zero, end];
                foreach (var range in applicable.SelectMany(r => r.Sources.Concat(r.Exclusions))
                    .Where(r => r.Begin.AddressFamily == local.AddressFamily))
                {
                    boundaries.Add(Number(range.Begin));
                    boundaries.Add(Number(range.End) + 1);
                }
                var points = boundaries.ToArray();

                // Keep the positive source ranges intact instead of permitting the inverse of each exclusion.
                foreach (var range in SelectRanges(points, bytes, source => permits.Any(r => r.Includes(source))))
                    result.Add(new(local, service, range, FirewallAction.Permit, FirewallPriority.Permit));

                // Only the exclusions left after considering ALL alternative permits become vetoes.
                // Independent MustNot predicates always veto; their own nested exceptions remain local.
                foreach (var range in SelectRanges(points, bytes, source =>
                    denies.Any(r => r.Matches(source))
                    || permits.Any(r => r.Includes(source)) && !permits.Any(r => r.Matches(source))))
                    result.Add(new(local, service, range, FirewallAction.Block, FirewallPriority.Deny));
            }

            return [.. result.Distinct()];
        }

        private sealed record SourceRule(FilterRuleType Type, IPAddressRange[] Sources,
            IPAddressRange[] Exclusions, bool Restricted)
        {
            internal bool Includes(IPAddress source) => !Restricted || Sources.Any(r => r.Contains(source));
            internal bool Matches(IPAddress source) => Includes(source) && !Exclusions.Any(r => r.Contains(source));
        }

        private static SourceRule CompileSource(PacketFilterRule rule)
        {
            if (rule is HostFilterRule host)
                return new(rule.Type, Ranges(host).ToArray(), [], true);

            if (rule is ServiceFilterRule transport)
            {
                var hosts = transport.HostRules.Select(h => (h.Type, Ranges: Ranges(h).ToArray())).ToArray();
                return new(rule.Type,
                    [.. hosts.Where(h => h.Type == FilterRuleType.Must).SelectMany(h => h.Ranges)],
                    [.. hosts.Where(h => h.Type == FilterRuleType.MustNot).SelectMany(h => h.Ranges)],
                    hosts.Any(h => h.Type == FilterRuleType.Must));
            }

            throw new NotSupportedException($"Firewall prototype cannot project {rule.GetType().Name}.");
        }

        private static IEnumerable<IPAddressRange> SelectRanges(BigInteger[] points, int bytes, Func<IPAddress, bool> matches)
        {
            BigInteger? start = null;
            for (int i = 0; i < points.Length - 1; i++)
            {
                bool selected = matches(Address(points[i], bytes));
                if (selected)
                    start ??= points[i];

                if (start is BigInteger first && (!selected || i == points.Length - 2))
                {
                    var last = (selected ? points[i + 1] : points[i]) - 1;
                    yield return new(Address(first, bytes), Address(last, bytes));
                    start = null;
                }
            }
        }

        private static IEnumerable<IPAddressRange> Ranges(HostFilterRule rule) => rule switch
        {
            StaticHostFilterRule host => host.Addresses.Select(ip => new IPAddressRange(ip)),
            StaticHostRangeFilterRule range => [range.Range],
            _ => throw new NotSupportedException($"Firewall prototype requires static source addresses, not {rule.GetType().Name}.")
        };

        private static BigInteger Number(IPAddress ip) => new(ip.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

        private static IPAddress Address(BigInteger value, int length)
        {
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            byte[] address = new byte[length];
            bytes.CopyTo(address, length - bytes.Length);
            return new(address);
        }
    }
}
