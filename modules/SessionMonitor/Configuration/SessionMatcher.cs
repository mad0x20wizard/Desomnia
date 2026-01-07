using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Configuration
{
    [TypeConverter(typeof(SessionMatcherConverter))]
    public class SessionMatcher
    {
        private List<string>? _patterns;

        private SessionMatcher()
        {

        }

        public SessionMatcher(string pattern)
        {
            _patterns = [pattern];
        }

        public SessionMatcher(IEnumerable<string> patterns)
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

        public static SessionMatcher None => new() { _patterns = [] };
        public static SessionMatcher Any => new() { _patterns = null };

        public static SessionMatcher operator +(SessionMatcher a, SessionMatcher? b)
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
                    return SessionMatcher.Any;
                else if (str == "false")
                    return SessionMatcher.None;
                else
                    return new SessionMatcher(str);
            }

            return null;
        }
    }
}
