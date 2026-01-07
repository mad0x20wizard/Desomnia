using CommandLine;

namespace MadWizard.Desomnia.Service.Installer.Options
{
    [Verb("read", HelpText = "Read configuration")]
    public class ConfigReadOptions
    {
        [Value(0, MetaName = "xml", Required = true, HelpText = "Path to configuration *.xml-file.")]
        public required string XmlFilePath { get; set; }

        [Value(1, MetaName = "ini", Required = false, HelpText = "Path to configuration *.ini-file.")]
        public required string IniFilePath { get; set; }
    }
}
