using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Interface;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.Environments
{
    /// <summary>
    /// Requires one or multiple network interfaces to exist, using the notation of the
    /// NetworkMonitor "interface" attribute (a regex matched against the interface id,
    /// or the exact interface name).
    ///
    /// The pattern may be suffixed with the operational states the interface has to be in,
    /// several of them separated by '|': interface="en0@up" is satisfied only while the
    /// interface is operational, interface="en0@up|dormant" also accepts a dormant one.
    /// Without a suffix the mere presence of the interface satisfies the condition,
    /// in whatever operational state it currently is.
    /// </summary>
    public sealed class InterfaceCondition : NetworkChangeCondition
    {
        const char STATUS_SEPARATOR = '@';
        const char STATUS_DELIMITER = '|';

        /// <summary>
        /// Configures the injected matcher from the attribute value. The matcher resolves
        /// from the persistent container, so a platform host that registered a matcher of
        /// its own (claiming the default) makes this condition platform-aware for free.
        /// </summary>
        public InterfaceCondition(InterfaceMatcher matcher, string value) : base(matcher)
        {
            var (pattern, statuses) = Parse(value);

            matcher.Interface = pattern;
            matcher.Status = statuses; // null accepts every status - mere presence
        }

        internal static (string Pattern, IReadOnlySet<OperationalStatus>? Statuses) Parse(string value)
        {
            string pattern = value;
            IReadOnlySet<OperationalStatus>? statuses = null;

            // split at the last separator, so that the pattern itself may contain one
            if (value.LastIndexOf(STATUS_SEPARATOR) is int index and >= 0)
            {
                pattern = value[..index];
                statuses = ParseStatuses(value[(index + 1)..], value);
            }

            if (string.IsNullOrWhiteSpace(pattern))
                throw new ConfigurationValueException($"Invalid interface '{value}'; the interface pattern must not be empty.");

            try
            {
                _ = new Regex(pattern); // validate early; matching happens per interface
            }
            catch (ArgumentException ex)
            {
                throw new ConfigurationValueException($"Invalid interface pattern '{pattern}' ({ex.Message})", ex);
            }

            return (pattern, statuses);
        }

        private static IReadOnlySet<OperationalStatus> ParseStatuses(string suffix, string value)
        {
            HashSet<OperationalStatus> statuses = [];

            foreach (var token in suffix.Split(STATUS_DELIMITER))
            {
                var name = token.Trim();

                // Enum.TryParse would also accept numbers and comma-separated lists,
                // silently turning a typo into a valid (but unintended) status
                if (name.Length == 0 || !name.All(char.IsLetter)
                    || !Enum.TryParse(name, ignoreCase: true, out OperationalStatus status))
                {
                    throw new ConfigurationValueException($"Invalid interface '{value}'; " +
                        $"'{name}' is not an operational status. Expected one or several of " +
                        $"{string.Join(", ", Enum.GetNames<OperationalStatus>().Select(status => status.ToLowerInvariant()))} " +
                        $"(separated by '{STATUS_DELIMITER}') behind the '{STATUS_SEPARATOR}'.");
                }

                statuses.Add(status);
            }

            return statuses;
        }
    }
}
