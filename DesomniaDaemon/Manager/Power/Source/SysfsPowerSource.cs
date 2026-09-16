using NLog;

namespace MadWizard.Desomnia.Power.Source
{
    /// <summary>
    /// Reads the power source from /sys/class/power_supply: on AC when any "Mains"
    /// supply reports online=1, on battery when Mains supplies exist but none is
    /// online. Systems without a Mains supply (desktops, VMs) report Unknown.
    /// Shared by the power managers and <see cref="SysfsPowerSourceProbe"/>.
    /// </summary>
    internal class SysfsPowerSource : PollingPowerSource
    {
        static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        const string POWER_SUPPLY_PATH = "/sys/class/power_supply";

        public override PowerSource Source => Read();

        public static PowerSource Read()
        {
            try
            {
                bool mains = false;

                foreach (var supply in Directory.EnumerateDirectories(POWER_SUPPLY_PATH))
                {
                    if (!ReadEntry(supply, "type").Equals("Mains", StringComparison.OrdinalIgnoreCase))
                        continue;

                    mains = true;

                    if (ReadEntry(supply, "online") == "1")
                        return PowerSource.AC;
                }

                return mains ? PowerSource.Battery : PowerSource.Unknown;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn(ex, $"Failed to read {POWER_SUPPLY_PATH}.");

                return PowerSource.Unknown;
            }
        }

        private static string ReadEntry(string supplyPath, string entry)
        {
            var path = Path.Combine(supplyPath, entry);

            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
    }
}
