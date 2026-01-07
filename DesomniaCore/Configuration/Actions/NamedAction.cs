using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Configuration
{
    [TypeConverter(typeof(NamedActionConverter))]
    public class NamedAction(string name, Arguments? args = null)
    {
        public string Name => name;

        public Arguments? Arguments => args;

        public override string ToString()
        {
            return name + Arguments?.ToString() ?? "";
        }
    }

    public class NamedActionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string str)
            {
                str = ExtractArguments(str, out Arguments? args);

                return new NamedAction(str, args);
            }

            return null;
        }

        protected string ExtractArguments(string str, out Arguments? args)
        {
            if (str.Contains('(') && str.Contains(')'))
            {
                int start = str.IndexOf("(") + "(".Length;
                int end = str.LastIndexOf(")");

                string inner = str[start..end];

                args = new Arguments(inner.Split(',').Select(arg => arg.Replace("'", "")).ToArray());

                str = str.Replace($"({inner})", "");
            }
            else
            {
                args = null;
            }

            return str;
        }
    }
}
