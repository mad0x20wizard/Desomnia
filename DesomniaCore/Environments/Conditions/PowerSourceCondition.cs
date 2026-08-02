using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Power.Source;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Requires the system to run on a designated power source (power="ac|battery").
    /// The platform hosts supply this condition from their PlatformModule, backed by
    /// their <see cref="IPowerSource"/> implementation.
    /// </summary>
    public sealed class PowerSourceCondition(string value) : IEnvironmentCondition
    {
        public required ILogger Logger { private get; init; }

        public required IPowerSource Power { private get; init; }

        readonly PowerSource _required = value.ToLowerInvariant() switch
        {
            "ac"        => PowerSource.AC,
            "battery"   => PowerSource.Battery,

            _ => throw new ConfigurationValueException($"Unknown s source '{value}'; expected \"ac\" or \"battery\"."),
        };

        bool _warned;

        public bool IsSatisfied()
        {
            var current = Power.Source;

            if (current == PowerSource.Unknown && !_warned)
            {
                Logger.LogWarning("The power source of this system could not be determined; treating power conditions as not matched.");

                _warned = true;
            }

            return current == _required;
        }

        event EventHandler? IEnvironmentCondition.Changed
        {
            add     => Power.SourceChanged += value;
            remove  => Power.SourceChanged -= value;
        }
    }
}
