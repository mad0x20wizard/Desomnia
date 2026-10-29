using Autofac;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Services;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTools;
using System.Net;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class ServicePolicyRegistrationTests
{
    private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");

    [Fact]
    public void SelectedServiceHasDirectAndInheritedRulesExactlyOnce()
    {
        var networkRule = new StaticHostRangeFilterRule
        {
            Type = FilterRuleType.Must, Range = IPAddressRange.Parse("192.0.2.0/24")
        };
        using var root = Container(builder => builder.RegisterInstance<FilterRule>(networkRule));
        var hostRule = new ServiceFilterRule { Service = new(IPProtocol.TCP, 80), Type = FilterRuleType.Must, Rules = [] };
        using var host = root.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag,
            builder => builder.RegisterInstance<FilterRule>(hostRule));
        var info = new WatchedServiceInfo { Name = "Web", Port = 80 };
        info.HostFilterRule.Add(new(Peer) { Type = FilterRuleType.Must });
        using var service = new NetworkServiceContext(host, info);
        var filter = service.Scope.Resolve<IFilter>();
        var rules = filter.Rules.ToArray();

        Assert.Equal(3, rules.Length);
        Assert.Same(networkRule, rules.Single(rule => ReferenceEquals(rule, networkRule)));
        Assert.Same(hostRule, Assert.Single(rules.OfType<ServiceFilterRule>()));
        Assert.Single(rules.OfType<StaticHostFilterRule>());
        Assert.False(filter.ShouldFilter(Context(Peer)));
        Assert.True(filter.ShouldFilter(Context(IPAddress.Parse("192.0.2.11"))));
        Assert.True(filter.ShouldFilter(Context(Peer, port: 81)));
    }

    [Fact]
    public void EmptyServiceDeclaresCaptureWithoutAddingAPositiveRule()
    {
        using var root = Container();
        using var service = new NetworkServiceContext(root, new WatchedServiceInfo { Name = "DNS", Protocol = IPProtocol.UDP, Port = 53 });
        var filter = service.Scope.Resolve<IFilter>();

        Assert.Empty(filter.Rules);
        Assert.False(filter.ShouldFilter(Context(Peer, IPProtocol.UDP, 53)));
        Assert.Equal(new UDPTrafficType(53), Assert.Single(CaptureTypes(service.Scope)));
    }

    [Theory]
    [InlineData(FilterRuleType.May)]
    [InlineData(FilterRuleType.MustNot)]
    public void CombinedConfigurationProducesSingleProtocolRules(FilterRuleType type)
    {
        using var root = Container();
        using var scope = new PolicyScope(root, [new ServiceFilterRuleInfo { Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 53, Type = type }]);
        var rules = scope.Rules.Cast<ServiceFilterRule>().ToArray();

        Assert.Equal(new[] { IPProtocol.TCP, IPProtocol.UDP }, rules.Select(rule => rule.Service.Protocol));
        Assert.All(rules, rule => Assert.Equal(type, rule.Type));
        Assert.Equal(type == FilterRuleType.May ? 2 : 0, CaptureTypes(scope.Lifetime).Length);
    }

    [Fact]
    public void MandatoryCombinedProtocolIsRejectedAtConfigurationBoundary()
    {
        using var root = Container();
        var error = Assert.Throws<FormatException>(() => new PolicyScope(root,
            [new ServiceFilterRuleInfo { Protocol = IPProtocol.TCP | IPProtocol.UDP, Port = 53, Type = FilterRuleType.Must }]));
        Assert.Contains("exactly one protocol", error.Message);
    }

    [Fact]
    public void RuntimeServiceRuleRejectsCombinedProtocols()
    {
        Assert.Throws<ArgumentException>(() => new ServiceFilterRule
        {
            Service = new(IPProtocol.TCP | IPProtocol.UDP, 53), Type = FilterRuleType.May, Rules = []
        });
    }

    [Theory]
    [InlineData(FilterRuleType.Must)]
    [InlineData(FilterRuleType.May)]
    public void PositiveServiceRuleRequestsItsActualTransport(FilterRuleType type)
    {
        using var root = Container();
        using var scope = new PolicyScope(root, [new ServiceFilterRuleInfo { Protocol = IPProtocol.TCP, Port = 22, Type = type }]);
        Assert.Equal(new TCPTrafficType(22), Assert.Single(CaptureTypes(scope.Lifetime)));
    }

    [Theory]
    [InlineData(FilterRuleType.Must)]
    [InlineData(FilterRuleType.MustNot)]
    public void HttpPredicatesAreDirectAndRequireDataWithoutUsageInspection(FilterRuleType type)
    {
        using var root = Container();
        var info = new WatchedHTTPServiceInfo
        {
            Name = "HTTP",
            RequestFilterRule = [new() { Path = "/private", Type = type }]
        };
        using var service = new NetworkServiceContext(root, info);
        var rule = Assert.IsType<HTTPRequestFilterRule>(Assert.Single(service.Scope.Resolve<IFilter>().Rules));
        Assert.Equal(type, rule.Type);
        Assert.Equal("/private", rule.Pattern.Path);
        Assert.Empty(rule.Rules);
        Assert.Equal(new TCPTrafficType(80, WithData: true), Assert.Single(CaptureTypes(service.Scope)));

        using var request = service.Scope.BeginLifetimeScope(MatchingScopeLifetimeTags.RequestLifetimeScopeTag);
        Assert.IsType<HTTPFilterContextParser>(request.Resolve<IFilterContextParser>());
        Assert.IsType<DefaultFilterContextParser>(root.Resolve<IFilterContextParser>());
    }

    [Theory]
    [InlineData(IPProtocol.Unknown)]
    [InlineData(IPProtocol.TCP | IPProtocol.UDP)]
    [InlineData((IPProtocol)4)]
    public void ServiceInfoRejectsInvalidProtocolsWhenCreatingRuntimeServices(IPProtocol protocol)
    {
        var info = new ServiceInfo { Name = "invalid", Protocol = protocol, Port = 80 };
        Assert.Throws<FormatException>(() => (IPService)info);
        Assert.Throws<FormatException>(() => info.Service);
    }

    [Theory]
    [InlineData(IPProtocol.TCP)]
    [InlineData(IPProtocol.UDP)]
    public void ServiceInfoPreservesSingleProtocol(IPProtocol protocol)
    {
        var info = new ServiceInfo { Name = "valid", Protocol = protocol, Port = 80 };
        Assert.Equal(new IPService(protocol, 80), (IPService)info);
        Assert.Contains(new IPService(protocol, 80), info.Service);
    }

    [Fact]
    public void TaggedStaticRangeIsRetainedAndParentRulesAreIsolated()
    {
        using var root = Container(builder => builder.Register<FilterRule>(_ => throw new InvalidOperationException("Unrelated rule resolved")));
        using var scope = new TaggedScope(root);
        var rule = Assert.IsType<StaticHostRangeFilterRule>(Assert.Single(scope.Rules));
        Assert.True(rule.Matches(Context(Peer)));
    }

    [Fact]
    public void CaptureRequestsEndWithTheirServiceScope()
    {
        using var root = Container();
        var service = new NetworkServiceContext(root, new WatchedServiceInfo { Name = "Web", Port = 80 });
        var request = Assert.Single(service.Scope.Resolve<IEnumerable<TrafficFilterRequest>>());
        bool disposed = false;
        request.Disposed += (_, _) => disposed = true;
        service.Dispose();
        Assert.True(disposed);
    }

    [Fact]
    public void EmptyCompositeAllowsButDoesNotHideUnavailableRegisteredFilters()
    {
        var builder = new ContainerBuilder();
        builder.RegisterComposite<CompositeFilter, IFilter>();
        using var root = builder.Build();
        Assert.False(root.Resolve<IFilter>().ShouldFilter(Context(Peer)));

        using var registered = Container();
        Assert.Throws<Autofac.Core.DependencyResolutionException>(() => registered.Resolve<IFilter>());
    }

    private static IContainer Container(Action<ContainerBuilder>? configure = null)
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        builder.RegisterType<DefaultFilterContextParser>().As<IFilterContextParser>().SingleInstance();
        builder.RegisterType<RuleFilter>().As<IFilter>().InstancePerMatchingLifetimeScope(
            MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, MatchingScopeLifetimeTags.NetworkServiceLifetimeScopeTag);
        builder.RegisterComposite<CompositeFilter, IFilter>();
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static IFilterContext Context(IPAddress peer, IPProtocol protocol = IPProtocol.TCP, ushort port = 80)
        => new ServiceCaptureFilterContext(new(peer, protocol, 50000), new(protocol, port)) { Direction = FlowDirection.Inbound };

    private static ITrafficType[] CaptureTypes(ILifetimeScope scope)
        => scope.Resolve<IEnumerable<TrafficFilterRequest>>().SelectMany(request => request.Types).ToArray();

    private sealed class PolicyScope : FilterContext
    {
        public PolicyScope(ILifetimeScope parent, IEnumerable<ServiceFilterRuleInfo> filters) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag,
                builder => RegisterServiceFilters(builder, filters));
        }
        public IEnumerable<FilterRule> Rules => Scope.Resolve<IFilter>().Rules;
        public ILifetimeScope Lifetime => Scope;
    }

    private sealed class TaggedScope : FilterContext
    {
        public TaggedScope(ILifetimeScope parent) : base(parent, "knock")
        {
            Scope = parent.BeginLifetimeScope(builder =>
            {
                RegisterTaggedPacketRuleFilter(builder);
                RegisterHostRangeFilter(builder, new HostRangeFilterRuleInfo(IPAddressRange.Parse("192.0.2.0/24"))
                {
                    Type = FilterRuleType.Must
                });
            });
        }
        public IEnumerable<FilterRule> Rules => Scope.Resolve<IFilter>().Rules;
    }
}
