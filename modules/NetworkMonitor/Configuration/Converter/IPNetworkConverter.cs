using System.ComponentModel;
using System.Globalization;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Converter
{
    public sealed class IPNetworkConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s)
                return IPNetwork.Parse(s);

            return base.ConvertFrom(context, culture, value);
        }
    }
}