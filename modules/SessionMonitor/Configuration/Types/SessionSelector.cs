using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Session.Configuration
{
    [TypeConverter(typeof(SessionMatcherConverter))]
    public class SessionSelector
    {
        private List<string>? _patterns;

        private SessionSelector()
        {

        }

        public SessionSelector(string pattern)
        {
            _patterns = [pattern];
        }

        public SessionSelector(IEnumerable<string> patterns)
        {
            _patterns = patterns.ToList();
        }

        public bool IsMatchingAny => _patterns == null;
        public bool IsMatchingNone => _patterns != null && _patterns.Count() == 0;
        public bool IsMatchingSelf => Match("self");

        public bool Match(string name)
        {
            if (IsMatchingAny)
                return true;

            foreach (string pattern in _patterns!)
                if (Regex.IsMatch(name, pattern))
                    return true;

            return false;
        }

        public static SessionSelector None => new() { _patterns = [] };
        public static SessionSelector Any => new() { _patterns = null };

        public static SessionSelector operator +(SessionSelector a, SessionSelector? b)
        {
            if (b == null)
                return a;

            if (a.IsMatchingAny || b.IsMatchingAny)
                return Any;

            return new() { _patterns = [.. a._patterns!, .. b._patterns!] };
        }
    }

    public class SessionMatcherConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string str)
            {
                if (str == "*" || str == "true")
                    return SessionSelector.Any;
                else if (str == "false")
                    return SessionSelector.None;
                else
                    return new SessionSelector(str);
            }

            return null;
        }
    }
}
