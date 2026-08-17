namespace MadWizard.Desomnia.Environments.Conditions
{
    /// <summary>
    /// The condition provider for the <c>environment:process</c> namespace: conditions on the
    /// process's environment variables (<c>env:USER="Kevin"</c> with
    /// <c>xmlns:env="environment:process"</c>). The process environment block is fixed for
    /// the life of the process, so these conditions never raise
    /// <see cref="IEnvironmentCondition.Changed"/> — platforms where environment-like state
    /// can change at runtime would contribute a provider under a different namespace.
    /// </summary>
    public sealed class ProcessEnvironmentConditionProvider : IEnvironmentConditionProvider
    {
        /// <summary>The namespace URI this provider registers under (exact match).</summary>
        public const string Namespace = "environment:process";

        public IEnvironmentCondition CreateCondition(string name, string value)
            => new EnvironmentVariableCondition(name, value);
    }

    /// <summary>
    /// Satisfied while the named environment variable has exactly the given value
    /// (case-sensitive). The empty value (<c>env:FLAG=""</c>) matches an unset or empty
    /// variable.
    /// </summary>
    public sealed class EnvironmentVariableCondition(string name, string value) : IEnvironmentCondition
    {
        public bool IsSatisfied()
        {
            var current = Environment.GetEnvironmentVariable(name);

            return value.Length == 0
                ? string.IsNullOrEmpty(current)
                : string.Equals(current, value, StringComparison.Ordinal);
        }

        /// <summary>Never raised - the process environment is fixed at start.</summary>
        public event EventHandler? Changed { add { } remove { } }
    }
}
