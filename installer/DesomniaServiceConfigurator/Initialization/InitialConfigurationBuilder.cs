using Autofac;
using System.Text;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Installer.Configuration
{
    public interface IInitialConfigurationBuilder
    {
        void Apply(InitializationPrefs ini, XDocument config);
    }

    internal class InitialConfigurationBuilder(InitializationPrefs prefs, string configFilePath) : IStartable
    {
        public required IEnumerable<IInitialConfigurationBuilder> Builders { private get; init; }

        void IStartable.Start()
        {
            XDocument document;
            if (File.Exists(configFilePath))
            {
                using var stream = new StreamReader(configFilePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                document = XDocument.Load(stream);
            }
            else
            {
                document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                    new XElement("DesomniaConfig", new XAttribute("version", 1)));
            }

            XElement root = document.Root!;

            if (prefs["SystemMonitor"]["timeout"] is string timeout)
                root.SetAttributeValue("timeout", timeout);
            else root.Attribute("timeout")?.Remove();

            if (prefs["SystemMonitor"]["idle"] is string idle)
                root.SetAttributeValue("onIdle", idle);
            else root.Attribute("onIdle")?.Remove();

            if (prefs["SystemMonitor"]["usage"] is string usage)
                root.SetAttributeValue("onDemand", usage);
            else root.Attribute("onDemand")?.Remove();

            foreach (var builder in Builders)
            {
                builder.Apply(prefs, document);
            }

            if (Path.GetDirectoryName(configFilePath) is string configDir && ! Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            using (var writer = new StreamWriter(configFilePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                document.Save(writer);
            }
        }
    }
}
