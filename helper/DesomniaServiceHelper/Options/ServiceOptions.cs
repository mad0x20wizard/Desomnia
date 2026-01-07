using CommandLine;

namespace MadWizard.Desomnia.Service.Helper.Options
{
    [Verb("control", HelpText = "Service control commands")]
    public class ServiceOptions
    {
        [Value(0, MetaName = "action", Required = true, HelpText = "Action to perform (e.g., init).")]
        public required string Command { get; set; }

        [Option('t', "timeout", Required = false, HelpText = "Path to the INI file.")]
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    }
}
