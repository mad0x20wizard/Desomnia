using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using MadWizard.Desomnia.Events;

namespace MadWizard.Desomnia.Configuration
{
    /// <summary>
    /// One configured action: EITHER a command expression OR a scheme-addressed URL —
    /// the constructors make the two kinds mutually exclusive, and the kind is decided
    /// solely by STRUCTURE, never by the attribute (§6.4): every action attribute
    /// accepts either form.
    /// </summary>
    [TypeConverter(typeof(ActionInfoConverter))]
    public class ActionInfo
    {
        public ActionInfo(CommandExpression command)
        {
            Command = command;
        }

        /// <summary>Convenience for the common command shape (a function name with
        /// optional arguments).</summary>
        public ActionInfo(string name, Arguments? args = null)
            : this(new CommandExpression(name, args)) { }

        /// <summary>A scheme-addressed action — it has no command; its parameters live
        /// inside the URL.</summary>
        public ActionInfo(Uri url)
        {
            URL = url;
        }

        public CommandExpression? Command { get; }

        public Uri? URL { get; }

        public override string ToString()
        {
            return URL?.OriginalString ?? Command?.ToString() ?? "";
        }

        /// <summary>THE border of the engine (§6.2): a configuration ActionInfo converts
        /// to its engine form here and never travels further. Null and blank-command
        /// infos (unset XML attributes) convert to null — AddAction treats that as a
        /// no-op, so <c>Usage.AddAction(config.OnUsage)</c> stays a one-liner.</summary>
        public static implicit operator EventAction?(ActionInfo? info) => EventAction.FromConfig(info);
    }

    public partial class ActionInfoConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type type)
        {
            return type == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string str)
            {
                if (TryParseURL(str, out Uri? url))
                    return new ActionInfo(url!);               // URLs skip argument extraction —
                                                               // parens/quotes are legal URL content
                str = ExtractArguments(str, out Arguments? args);

                return new ActionInfo(str, args);
            }

            return null;
        }

        /// <summary>The structure rule (§6.4): at least TWO scheme characters before a
        /// colon (drive-letter paths never qualify), "//" optional (mailto:-style works).
        /// '+' is not a scheme character here — it is the schedule separator.</summary>
        protected static bool TryParseURL(string value, out Uri? url)
        {
            url = null;

            if (!URLShapePattern().IsMatch(value))
                return false;

            return Uri.TryCreate(value, UriKind.Absolute, out url);
        }

        [GeneratedRegex("^[A-Za-z][A-Za-z0-9.-]+:")]
        private static partial Regex URLShapePattern();

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
