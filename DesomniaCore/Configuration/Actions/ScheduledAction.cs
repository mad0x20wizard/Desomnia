using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Configuration
{
    [TypeConverter(typeof(ScheduledActionConverter))]
    public class ScheduledAction(string name, Arguments? args, TimeSpan delay) : DelayedAction(name, args)
    {
        public override bool HasDelay => delay > TimeSpan.Zero;

        public TimeSpan Delay => delay;
    }

    public class ScheduledActionConverter : DelayedActionConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s && !s.Contains('+'))
                value += $"+0s";

            if (base.ConvertFrom(context, culture, value) is ScheduledAction action)
                return action;

            throw new FormatException($"Cannot convert {value} to ScheduledAction");
        }

    }
}
