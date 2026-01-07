using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Configuration
{
    [TypeConverter(typeof(ThrottledActionConverter))]
    public class ThrottledAction(string name, Arguments? args, uint times) : DelayedAction(name, args)
    {
        public override bool HasDelay => times > 0;

        public uint Times => times;
    }

    public class ThrottledActionConverter : DelayedActionConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s && !s.Contains('+'))
                value += $"+0x";

            if (base.ConvertFrom(context, culture, value) is ThrottledAction action)
                return action;

            throw new FormatException($"Cannot convert {value} to ThrottledAction");
        }
    }
}
