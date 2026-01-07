using Autofac;
using System.Text;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Installer.Configuration
{
    public interface IInitialConfigurationReader
    {
        void Apply(XDocument config, InitializationPrefs ini);
    }

    internal class InitialConfigurationReader(string configFilePath, InitializationPrefs prefs) : IStartable
    {
        public required IEnumerable<IInitialConfigurationReader> Readers { private get; init; }

        void IStartable.Start()
        {
            using var stream = new StreamReader(configFilePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var document = XDocument.Load(stream);

            prefs["SystemMonitor"]["timeout"] = document.Root?.Attribute("timeout")?.Value;
            prefs["SystemMonitor"]["idle"] = document.Root?.Attribute("onIdle")?.Value;
            prefs["SystemMonitor"]["usage"] = document.Root?.Attribute("onDemand")?.Value;

            foreach (var reader in Readers)
            {
                reader.Apply(document, prefs);
            }
        }
    }
}
