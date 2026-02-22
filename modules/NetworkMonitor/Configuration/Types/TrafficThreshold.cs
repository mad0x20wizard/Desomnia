using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.Configuration
{
    [TypeConverter(typeof(TrafficThresholdConverter))]
    public struct TrafficThreshold
    {
        public long?    TrafficUnit { get; set; }
        public TimeSpan?   TimeUnit { get; set; }

        public long           Value { get; set; }
    }

    public partial class TrafficThresholdConverter : TypeConverter
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

        private static TrafficThreshold TryParseFormat(string str)
        {
            str = string.Concat(str.Where(c => !char.IsWhiteSpace(c))); // remove all whitespace

            if (TrafficThresholdPattern().Match(str) is Match match)
            {
                var speed = new TrafficThreshold { Value = long.Parse(match.Groups["Value"].Value) };

                if (match.Groups.TryGetValue("TrafficUnit", out var traffic) && traffic.Success)
                {
                    speed.TrafficUnit = traffic.Value.ToUpper() switch
                    {
                        "B"     => 1L,
                        "KB"    => 1L << 10,
                        "MB"    => 1L << 20,
                        "GB"    => 1L << 30,
                        "TB"    => 1L << 40,
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

        [GeneratedRegex(@"^\s*(?<Value>\d+)\s*(?<TrafficUnit>(?:[kKmMgGtT]?[bB]))?\s*(?:/\s*(?<TimeUnit>ms|s|min|h|d))?\s*$")]
        private static partial Regex TrafficThresholdPattern();

    }

}
