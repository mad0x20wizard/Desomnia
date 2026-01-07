using CommandLine;

namespace MadWizard.Desomnia.Daemon.Options
{
    internal class CommandLineOptions
    {
        [Option('a', "auto-reload", Required = false, Default = false, HelpText = "Enable automatic reloading after config file changed.")]
        public bool AutoReload { get; set; }

        [Option('p', "auto-reload-path", Required = false, HelpText = "Enable automatic reloading after config file changed (in a different directory).")]
        public string? AutoReloadPath { get; set; }

    }
}
