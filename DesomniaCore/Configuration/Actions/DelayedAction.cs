using Microsoft.Extensions.Configuration.Xml;
using System.ComponentModel;
using System.Globalization;

namespace MadWizard.Desomnia.Configuration
{
    [TypeConverter(typeof(DelayedActionConverter))]
    public class DelayedAction(string name, Arguments? args = null) : NamedAction(name, args)
    {
        public virtual bool HasDelay => false;
    }

    public class DelayedActionConverter : NamedActionConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string str)
            {
                if (str.Contains('+'))
                {
                    if (str.EndsWith('x'))
                    {
                        str = ExtractTimes(str, out uint times);
                        str = ExtractArguments(str, out Arguments? args);

                        return new ThrottledAction(str, args, times);
                    }
                    else
                    {
                        str = ExtractDelay(str, out TimeSpan delay);
                        str = ExtractArguments(str, out Arguments? args);

                        return new ScheduledAction(str, args, delay);
                    }
                }
                else
                {
                    str = ExtractArguments(str, out Arguments? args);

                    return new DelayedAction(str, args);
                }
            }

            return base.ConvertFrom(context, culture, value);
        }

        private string ExtractDelay(string str, out TimeSpan delay)
        {
            if (str.Contains('+'))
            {
                var split = str.Split('+');

                delay = TimeSpan.Parse(CustomXmlConfigurationProvider.NormalizeTimeSpanVariations(split[1]));

                return split[0];
            }

            delay = TimeSpan.Zero;

            return str;
        }

        private string ExtractTimes(string str, out uint times)
        {
            if (str.Contains('+') && str.EndsWith('x'))
            {
                var split = str.Split('+');

                times = uint.Parse(split[1].Replace("x", ""));

                return split[0];
            }

            times = 0;

            return str;
        }

    }
}
