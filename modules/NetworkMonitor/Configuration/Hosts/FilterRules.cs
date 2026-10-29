using MadWizard.Desomnia.Network.Configuration.Filter;
using Nito.Disposables.Internals;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public interface IFilterInfo
    {
        public IEnumerable<EveryHostFilterRuleInfo> AnyHostFilterRules
            => Enumerable.Empty<EveryHostFilterRuleInfo>()
                .Concat(new[] { ForeignHostFilterRule }.WhereNotNull())
                .Concat(new[] { EveryHostFilterRule }.WhereNotNull());

        public IEnumerable<ServiceFilterRuleInfo> ServiceFilterRules
            => ServiceFilterRule;

        // Host filter rules
        public IList<HostFilterRuleInfo>        HostFilterRule          { get; }
        public IList<HostRangeFilterRuleInfo>   HostRangeFilterRule     { get; }
        public ForeignHostFilterRuleInfo?       ForeignHostFilterRule   { get; }
        public EveryHostFilterRuleInfo?         EveryHostFilterRule     { get; }

        // Service filter rules
        public IList<ServiceFilterRuleInfo>     ServiceFilterRule       { get; }
        public PingFilterRuleInfo?              PingFilterRule          { get; }
    }

    public record class SharedFilterRules : IFilterInfo
    {
        // Host filter rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; set; } = [];
        public ForeignHostFilterRuleInfo? ForeignHostFilterRule { get; set; }
        public EveryHostFilterRuleInfo? EveryHostFilterRule { get; set; }

        // Service filter rules
        public IList<ServiceFilterRuleInfo> ServiceFilterRule { get; set; } = [];
        public PingFilterRuleInfo? PingFilterRule { get; set; }
    }
}
