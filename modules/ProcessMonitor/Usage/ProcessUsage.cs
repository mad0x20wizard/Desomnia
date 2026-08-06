namespace MadWizard.Desomnia.Processes
{
    /// <summary>
    /// What the group measured, in the unit it was measured in: each threshold fills exactly one
    /// of its pair – the relative or the absolute member, the amount or the rate – and only a
    /// filled member is rendered. A watch without thresholds fills nothing and renders bare.
    /// </summary>
    public class ProcessUsage(string? name) : UsageToken
    {
        public string? Name => name;

        public ProcessUsageMetrics? Metrics { get; init; }

        public override string ToString() => Name != null
            ? "{" + Name + (Metrics?.ToString() is { Length: > 0 } parts ? " @ " + parts : "") + "}"
            : "{{" + Metrics?.ToString() + "}}";
    }
}
