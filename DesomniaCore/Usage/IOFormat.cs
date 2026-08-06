using System.Globalization;

namespace MadWizard.Desomnia
{
    /**
     * How a measured transfer is written back to the user, and the mirror image of the threshold
     * grammar it will be read next to: the magnitudes step in the same binary thousands the
     * parser reads, so a group that moved exactly what "10Mbit/s" asked for is logged as
     * 10Mbit/s and not as some decimal neighbour of it.
     *
     * Amounts are bytes and rates are bits, because that is how each is spoken about: a file is
     * so many megabytes, a line is so many megabit per second – every datasheet, tariff and speed
     * test quotes transmission in bits, and a log that answered "5MB/s" to a "5Mbit/s" threshold
     * would read like a different measurement rather than the same one.
     */
    public static class IOFormat
    {
        /// <summary>An amount, in bytes – what an absolute threshold compared.</summary>
        public static string Bytes(double bytes) => Magnitude(bytes, "0.0", "B", "kB", "MB", "GB");

        /// <summary>A transfer rate, in bits per second – what a threshold with a time unit compared.</summary>
        public static string BitsPerSecond(double bytesPerSecond) => Magnitude(bytesPerSecond * 8, "0.#", "bit", "kbit", "Mbit", "Gbit") + "/s";

        /**
         * Picks the magnitude on the *rounded* value: a rate of 1023.6 belongs to the next unit
         * up, because the whole-number format the smallest one uses would otherwise round it to
         * a "1024B" that unit can no longer mean.
         */
        private static string Magnitude(double value, string format, string one, string kilo, string mega, string giga) => Math.Round(value) switch
        {
            // invariant culture: the log speaks the threshold grammar's language ("69.3"),
            // regardless of the host's locale
            < 1L << 10 => value.ToString("0", CultureInfo.InvariantCulture) + one,
            < 1L << 20 => (value / 1024.0).ToString(format, CultureInfo.InvariantCulture) + kilo,
            < 1L << 30 => (value / 1048576.0).ToString(format, CultureInfo.InvariantCulture) + mega,
            _          => (value / 1073741824.0).ToString(format, CultureInfo.InvariantCulture) + giga
        };
    }
}
