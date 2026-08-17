using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Configuration
{
    [TypeConverter(typeof(IOThresholdConverter))]
    public struct TransmissionThreshold
    {
        public long?        ByteUnit { get; set; }
        public TimeSpan?    TimeUnit { get; set; }

        public long         Amount  { get; set; }
    }

    public partial class IOThresholdConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string str)
            {
                return TryParseFormat(str);
            }

            return null;
        }

        private static TransmissionThreshold TryParseFormat(string str)
        {
            str = string.Concat(str.Where(c => !char.IsWhiteSpace(c))); // remove all whitespace

            // Match never returns null – only Success separates "1MB/s" from garbage, and without
            // this check the descriptive error below was dead code behind a long.Parse("") throw
            if (TrafficThresholdPattern().Match(str) is { Success: true } match)
            {
                var speed = new TransmissionThreshold { Amount = long.Parse(match.Groups["Value"].Value) };

                if (match.Groups.TryGetValue("TrafficUnit", out var traffic) && traffic.Success)
                {
                    // invariant on purpose: under tr-TR the ordinary ToUpper turns "bit" into
                    // "BİT", which would fall past every case into the unit-less packet reading
                    speed.ByteUnit = traffic.Value.ToUpperInvariant() switch
                    {
                        "B"     => 1L,
                        "BYTE"  => 1L,

                        "KB"    => 1L << 10,
                        "KBYTE" => 1L << 10,

                        "MB"    => 1L << 20,
                        "MBYTE" => 1L << 20,

                        "GB"    => 1L << 30,
                        "GBYTE" => 1L << 30,

                        "TB"    => 1L << 40,
                        "TBYTE" => 1L << 40,

                        // the same binary magnitudes, eight to the byte – a lone "bit" is refused
                        // because it is less than the one byte a long can resolve, and because a
                        // unit-less number already means something else to the network side
                        "KBIT"  => (1L << 10) / 8,
                        "MBIT"  => (1L << 20) / 8,
                        "GBIT"  => (1L << 30) / 8,
                        "TBIT"  => (1L << 40) / 8,

                        "BIT"   => throw new FormatException("Bit units need a magnitude (e.g. \"5Mbit\")"),

                        _       => null
                    };
                }

                if (match.Groups.TryGetValue("TimeUnit", out var time) && time.Success)
                {
                    speed.TimeUnit = time.Value switch
                    {
                        "ms"    => TimeSpan.FromMilliseconds(1),
                        "s"     => TimeSpan.FromSeconds(1),
                        "min"   => TimeSpan.FromMinutes(1),
                        "h"     => TimeSpan.FromHours(1),
                        "d"     => TimeSpan.FromDays(1),
                        _       => null
                    };
                }

                return speed;
            }

            throw new FormatException("Invalid traffic threshold format");
        }

        [GeneratedRegex(@"^\s*(?<Value>\d+)\s*(?<TrafficUnit>[kKmMgGtT]?(?:[bB](?:[iI][tT])?|[bB][yY][tT][eE]))?\s*(?:/\s*(?<TimeUnit>ms|s|min|h|d))?\s*$")]
        private static partial Regex TrafficThresholdPattern();

    }

}
