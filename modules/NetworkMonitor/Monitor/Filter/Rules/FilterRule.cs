using MadWizard.Desomnia.Network.Monitor.Filter.Rules;
using System.Collections.Concurrent;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class FilterRule
    {
        public required FilterRuleType Type { get; init; }

        public abstract bool Wants(IFilterContext context);
        public abstract bool Needs<N>() where N : IFilterContext;

        public abstract bool TryMatch(IFilterContext context, out bool matches);

        // Resolve the whole rule set before requesting data so definitive rejections take precedence.
        protected internal static bool MatchesRules(IEnumerable<FilterRule> rules, IFilterContext context, FilterRuleNeeds needs)
        {
            bool pending = false;
            bool pendingMay = false;
            foreach (var rule in rules)
            {
                // Null means this context branch could still supply the missing capability.
                bool? match = rule.TryMatch(context, out bool result) ? result : null;
                if (match is null && !rule.Wants(context))
                    match = false;

                switch (rule.Type)
                {
                    case FilterRuleType.Must    when match is false:
                    case FilterRuleType.MustNot when match is true:
                        return false;

                    case FilterRuleType.Must:
                        needs.Must--;
                        pending |= match is null;
                        break;

                    case FilterRuleType.MustNot:
                        pending |= match is null;
                        break;

                    case FilterRuleType.May:
                        needs.May &= match is not true;
                        pendingMay |= match is null;
                        break;
                }
            }

            if (needs.Must != 0 || needs.May && !pendingMay)
                return false;

            if (pending |= needs.May && pendingMay)
            {
                if (context.Direction == FlowDirection.Inbound)
                {
                    if (needs.IPTraffic && context is not IIPFilterContext)
                    {
                        throw new IPTrafficNeededException();
                    }

                    if (needs.ServicePayload && context is IIPServiceFilterContext ips and not IIPServicePayloadFilterContext)
                    {
                        throw new IPServicePayloadNeededException(ips.Service, ips.RemoteEndPoint);
                    }
                }

                return false;
            }

            return true;
        }
    }

    public abstract class FilterRule<T> : FilterRule where T : IFilterContext
    {
        private static readonly ConcurrentDictionary<Type, bool> CompatibleContexts = new();

        public override bool Wants(IFilterContext context) => CompatibleContexts.GetOrAdd(context.GetType(), static type =>
            type.GetInterfaces().Where(capability => typeof(IFilterContext).IsAssignableFrom(capability))
                .All(capability => capability.IsAssignableFrom(typeof(T))));

        public override bool Needs<N>() => typeof(N).IsAssignableFrom(typeof(T));

        public sealed override bool TryMatch(IFilterContext context, out bool matches)
        {
            if (context is T supported)
            {
                matches = Matches(supported);

                return true;
            }
            else
            {
                matches = false;

                return false;
            }
        }

        public abstract bool Matches(T context);
    }

    public abstract class FilterRule<T, R> : FilterRule<T> where T : IFilterContext where R : FilterRule
    {
        readonly FilterRuleNeeds _needs;

        public required IEnumerable<R> Rules
        {
            get; init
            {
                _needs = new(field = value);
            }
        }

        public override bool Matches(T context)
        {
            return MatchesRules(Rules, context, _needs);
        }
    }
}
