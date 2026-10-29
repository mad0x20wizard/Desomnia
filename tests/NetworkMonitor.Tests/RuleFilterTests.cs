using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using System.Net;
using Xunit;
using static MadWizard.Desomnia.Network.Tests.FilterParserPackets;

namespace MadWizard.Desomnia.Network.Tests;

public class RuleFilterTests
{
    private sealed class Predicate<T>(bool matches) : FilterRule<T> where T : IFilterContext
    {
        public int Calls { get; private set; }
        public override bool Matches(T context) { Calls++; return matches; }
    }

    private static Predicate<T> Rule<T>(FilterRuleType type, bool matches = true) where T : IFilterContext
        => new(matches) { Type = type };

    private static IFilterContext Address(FlowDirection direction = FlowDirection.Inbound)
        => new CaptureFilterContext(Peer) { Direction = direction };

    private static IFilterContext Service(FlowDirection direction = FlowDirection.Inbound)
        => new ServiceCaptureFilterContext(new(Peer, IPProtocol.TCP, 50000), new(IPProtocol.TCP, 80)) { Direction = direction };

    private static IFilterContext Payload()
        => new DefaultFilterContextParser().CreateContext([Tcp(100, "not HTTP")], FlowDirection.Inbound);

    public static IEnumerable<object[]> PositiveCombinations()
    {
        for (int mask = 0; mask < 16; mask++)
            yield return [ (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0 ];
    }

    [Theory]
    [MemberData(nameof(PositiveCombinations))]
    public void EveryMustAndAtLeastOneMayMustMatch(bool firstMust, bool secondMust, bool firstMay, bool secondMay)
    {
        var rules = new FilterRule[] {
            Rule<IFilterContext>(FilterRuleType.Must, firstMust), Rule<IFilterContext>(FilterRuleType.Must, secondMust),
            Rule<IFilterContext>(FilterRuleType.May, firstMay), Rule<IFilterContext>(FilterRuleType.May, secondMay)
        };
        bool filtered = !(firstMust && secondMust && (firstMay || secondMay));
        var filter = new RuleFilter(rules);
        Assert.Equal(filtered, filter.ShouldFilter(Address()));
        Assert.Equal(filtered, filter.ShouldFilter(Address())); // cached requirements are not consumed
        Assert.Equal(filtered, new RuleFilter(rules.Reverse().ToArray()).ShouldFilter(Address()));
    }

    [Fact]
    public void EmptyAndMatchingMustOnlySetsAllowAndVetoWins()
    {
        Assert.False(new RuleFilter([]).ShouldFilter(Address()));
        Assert.False(new RuleFilter([Rule<IFilterContext>(FilterRuleType.Must)]).ShouldFilter(Address()));
        Assert.False(new RuleFilter([Rule<IFilterContext>(FilterRuleType.MustNot, false)]).ShouldFilter(Address()));
        Assert.True(new RuleFilter([Rule<IFilterContext>(FilterRuleType.Must),
            Rule<IFilterContext>(FilterRuleType.May), Rule<IFilterContext>(FilterRuleType.MustNot)]).ShouldFilter(Address()));
    }

    [Fact]
    public void RequirementsFollowTheMinimumContextType()
    {
        var host = new RuleFilter([Rule<IFilterContext>(FilterRuleType.Must)]);
        Assert.False(host.Needs.IPTraffic);
        Assert.False(host.Needs.ServicePayload);
        var service = new RuleFilter([Rule<IIPServiceFilterContext>(FilterRuleType.Must)]);
        Assert.True(service.Needs.IPTraffic);
        Assert.False(service.Needs.ServicePayload);
        var http = new RuleFilter([Rule<IHTTPFilterContext>(FilterRuleType.Must)]);
        Assert.True(http.Needs.IPTraffic);
        Assert.True(http.Needs.ServicePayload);
    }

    [Theory]
    [InlineData(FilterRuleType.Must)]
    [InlineData(FilterRuleType.May)]
    [InlineData(FilterRuleType.MustNot)]
    public void UnresolvedCompatibleRulesAcquireIPThenPayloadWithoutCallingPredicate(FilterRuleType type)
    {
        var rule = Rule<IHTTPFilterContext>(type);
        var filter = new RuleFilter([rule]);
        Assert.Throws<IPTrafficNeededException>(() => filter.ShouldFilter(Address()));
        var needed = Assert.Throws<IPServicePayloadNeededException>(() => filter.ShouldFilter(Service()));
        Assert.Equal(new IPService(IPProtocol.TCP, 80), needed.Service);
        Assert.Equal(new IPServiceEndPoint(Peer, IPProtocol.TCP, 50000), needed.RemoteEndPoint);
        Assert.Equal(0, rule.Calls);
        Assert.True(filter.ShouldFilter(Payload())); // a generic payload cannot satisfy HTTP
        Assert.Equal(0, rule.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownRejectionWinsOverPendingRequirementsInEitherOrder(bool pendingFirst)
    {
        foreach (var veto in new[] { Rule<IFilterContext>(FilterRuleType.MustNot), Rule<IFilterContext>(FilterRuleType.Must, false),
            Rule<IFilterContext>(FilterRuleType.May, false) })
        {
            var pending = Rule<IHTTPFilterContext>(FilterRuleType.Must);
            var filter = new RuleFilter(pendingFirst ? [pending, veto] : [veto, pending]);
            Assert.True(filter.ShouldFilter(Address()));
            Assert.True(filter.ShouldFilter(Service()));
            Assert.Equal(0, pending.Calls);
        }
    }

    [Fact]
    public void MatchingMayAvoidsAcquiringOtherAlternativesButNotMandatoryOrVetoRules()
    {
        var alternative = Rule<IHTTPFilterContext>(FilterRuleType.May);
        var matching = Rule<IFilterContext>(FilterRuleType.May);
        Assert.False(new RuleFilter([alternative, matching]).ShouldFilter(Address()));
        Assert.False(new RuleFilter([matching, alternative]).ShouldFilter(Address()));
        Assert.Throws<IPTrafficNeededException>(() => new RuleFilter([matching,
            Rule<IHTTPFilterContext>(FilterRuleType.MustNot)]).ShouldFilter(Address()));
        Assert.Throws<IPTrafficNeededException>(() => new RuleFilter([matching,
            Rule<IHTTPFilterContext>(FilterRuleType.Must)]).ShouldFilter(Address()));
    }

    [Theory]
    [InlineData(FilterRuleType.Must, true)]
    [InlineData(FilterRuleType.May, true)]
    [InlineData(FilterRuleType.MustNot, false)]
    public void SiblingContextBranchesNeverRequestDataOrInvokeTheRule(FilterRuleType type, bool filtered)
    {
        IFilterContext icmp = new ICMPCaptureFilterContext(Peer, ICMPType.Echo) { Direction = FlowDirection.Inbound };
        var http = Rule<IHTTPFilterContext>(type);
        Assert.False(http.Wants(icmp)); // the variable's declared type must not hide the ICMP branch
        Assert.Equal(filtered, new RuleFilter([http]).ShouldFilter(icmp));
        Assert.Equal(0, http.Calls);
        var ping = Rule<IICMPFilterContext>(type);
        Assert.Equal(filtered, new RuleFilter([ping]).ShouldFilter(Service()));
        Assert.Equal(0, ping.Calls);
    }

    [Theory]
    [InlineData(FilterRuleType.Must)]
    [InlineData(FilterRuleType.May)]
    [InlineData(FilterRuleType.MustNot)]
    public void OutboundAccountingDoesNotAcquireMissingData(FilterRuleType type)
    {
        var filter = new RuleFilter([Rule<IHTTPFilterContext>(type)]);
        Assert.True(filter.ShouldFilter(Address(FlowDirection.Outbound)));
        Assert.True(filter.ShouldFilter(Service(FlowDirection.Outbound)));
    }

    [Fact]
    public void ServiceAndHostMustRulesCanMatchTogetherAndNestedMayStillApplies()
    {
        var host = new StaticHostFilterRule { Type = FilterRuleType.Must, Addresses = [Peer] };
        var service = new ServiceFilterRule { Type = FilterRuleType.Must, Service = new(IPProtocol.TCP, 80),
            Rules = [host, new StaticHostFilterRule { Type = FilterRuleType.May, Addresses = [Watched] }] };
        Assert.True(new RuleFilter([host, service]).ShouldFilter(Service()));
        service = new ServiceFilterRule { Type = FilterRuleType.Must, Service = new(IPProtocol.TCP, 80), Rules = [host] };
        Assert.False(new RuleFilter([host, service]).ShouldFilter(Service()));
    }

    [Fact]
    public void SpecializedRuleRunsOnlyOnceItsContextExists()
    {
        var rule = Rule<IHTTPFilterContext>(FilterRuleType.Must);
        var context = new HTTPFilterContextParser().CreateContext([Tcp(100, "GET / HTTP/1.1\r\nHost: test\r\n\r\n")], FlowDirection.Inbound);
        Assert.False(new RuleFilter([rule]).ShouldFilter(context));
        Assert.Equal(1, rule.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompositeDefersAcquisitionUntilEveryFilterCanVeto(bool pendingFirst)
    {
        IFilter pending = new RuleFilter([Rule<IHTTPFilterContext>(FilterRuleType.Must)]);
        IFilter veto = new RuleFilter([Rule<IFilterContext>(FilterRuleType.MustNot)]);
        IFilter composite = new CompositeFilter(pendingFirst ? [pending, veto] : [veto, pending]);
        Assert.True(composite.ShouldFilter(Address()));
        composite = new CompositeFilter([pending, new RuleFilter([])]);
        Assert.Throws<IPTrafficNeededException>(() => composite.ShouldFilter(Address()));
    }
}
