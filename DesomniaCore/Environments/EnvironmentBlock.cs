using MadWizard.Desomnia.Configuration.Model;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>One &lt;Environment&gt; (or &lt;DefaultEnvironment&gt;) block of the configuration.</summary>
    internal sealed class EnvironmentBlock
    {
        /// <summary>The custom name, the block's conditions (<c>lid="closed" power="ac"</c>),
        /// "anonymous #N" or "default" - used for logging.</summary>
        public required string DisplayName { get; init; }

        public string? Name { get; init; }

        public bool IsDefault { get; init; }

        /// <summary>When the block merges (see the onlyIf attribute).</summary>
        public EnvironmentMergeMode MergeMode { get; init; } = EnvironmentMergeMode.Always;

        /// <summary>Decides value conflicts between blocks - higher priority supersedes (see the priority attribute).</summary>
        public int Priority { get; init; }

        /// <summary>Name of another environment that must be applied for this block to apply
        /// (see the onlyIf attribute, when its value names an environment instead of a merge mode).</summary>
        public string? OnlyIf { get; init; }

        /// <summary>Name of another environment that must not be applied for this block to apply (see the onlyIfNot attribute).</summary>
        public string? OnlyIfNot { get; init; }

        /// <summary>Condition attributes (everything that is not structural), in document order.</summary>
        public required IReadOnlyList<ConditionAttribute> ConditionAttributes { get; init; }

        /// <summary>The block's content, normalized to an abstract &lt;SystemMonitor&gt; container.</summary>
        public required ConfigNode Content { get; init; }

        /// <summary>Resolved from <see cref="ConditionAttributes"/> by the module hook.</summary>
        public IReadOnlyList<IEnvironmentCondition> Conditions { get; set; } = [];

        /// <summary>
        /// Whether all conditions are satisfied. The block's actual inclusion is decided
        /// by the monitor, since <see cref="MergeMode"/> may depend on the other blocks.
        /// </summary>
        public bool IsActive => Conditions.All(condition => condition.IsSatisfied());
    }
}
