using CommandLine;

namespace MadWizard.Desomnia.Service.Helper.Options
{
    [Verb("read", HelpText = "Read commands")]
    public class ReadOptions
    {
        [Value(0, MetaName = "value", Required = true)]
        public required string Value { get; set; }
    }
}
