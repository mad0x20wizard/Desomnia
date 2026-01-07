using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Installer.Configuration
{
    internal class NetworkMonitorConfigurator : IInitialConfigurationBuilder, IInitialConfigurationReader
    {
        void IInitialConfigurationBuilder.Apply(InitializationPrefs ini, XDocument config)
        {
            // no safe auto config possible
            if (config.Root!.Elements("NetworkMonitor").Count() > 1)
                return;

            if (ini["NetworkMonitor"]["interface"] is string id)
            {
                // add new network monitor config
                if (config.Root?.Element("NetworkMonitor") is not XElement network)
                {
                    network = new XElement("NetworkMonitor");

                    config.Root?.Add(network);
                }

                network.SetAttributeValue("interface", id);

                if (ini["NetworkMonitor"]["name"] is string name)
                {
                    network.SetAttributeValue("name", name);
                }
            }
            else // effectively disable the network monitor
            {
                if (config.Root?.Element("NetworkMonitor") is XElement network)
                {
                    network.Attribute("interface")?.Remove();
                    network.Attribute("name")?.Remove();
                }
            }
        }

        void IInitialConfigurationReader.Apply(XDocument config, InitializationPrefs ini)
        {
            ini["NetworkMonitor"]["count"] = config.Root?.Elements("NetworkMonitor").Count().ToString();

            if (config.Root?.Elements("NetworkMonitor").FirstOrDefault() is XElement monitor)
                ini["NetworkMonitor"]["interface"] = monitor.Attribute("interface")?.Value;
        }
    }
}
