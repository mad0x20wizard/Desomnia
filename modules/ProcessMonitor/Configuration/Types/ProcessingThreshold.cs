using MadWizard.Desomnia.Configuration;
using System.ComponentModel;
using System.Globalization;

namespace MadWizard.Desomnia.Processes.Configuration
{
    /// <summary>
    /// A threshold against a processing-time counter – the CPU's or the GPU's, which is why it
    /// is not named after either: "10%" compares the group's share of the interval, "10s" the
    /// absolute time it consumed within one.
    /// </summary>
    [TypeConverter(typeof(ProcessingThresholdConverter))]
    public readonly struct ProcessingThreshold
    {
        public double?      RelativeUsage   { get; private init; }
        public TimeSpan?    AbsoluteTime    { get; private init; }

        public ProcessingThreshold(double usage)
        {
            RelativeUsage = usage;
        }

        public ProcessingThreshold(TimeSpan time)
        {
            AbsoluteTime = time;
        }
    }

    public partial class ProcessingThresholdConverter : TypeConverter
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

        private static ProcessingThreshold TryParseFormat(string str)
        {
            str = string.Concat(str.Where(c => !char.IsWhiteSpace(c))); // remove all whitespace

            if (TimeSpan.TryParse(ValueVariations.NormalizeTimeSpan(str), CultureInfo.InvariantCulture, out var time))
            {
                return new ProcessingThreshold(time);
            }
            else if (str.EndsWith('%') && double.TryParse(str[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var usage))
            {
                return new ProcessingThreshold(usage / 100.0);
            }

            throw new FormatException("Invalid processing threshold format");
        }
    }
}
