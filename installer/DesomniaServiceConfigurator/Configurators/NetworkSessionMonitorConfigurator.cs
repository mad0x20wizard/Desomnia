using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Installer.Configuration
{
    internal class NetworkSessionMonitorConfigurator : IInitialConfigurationBuilder, IInitialConfigurationReader
    {
        void IInitialConfigurationBuilder.Apply(InitializationPrefs ini, XDocument config)
        {
            if (ini["NetworkSessionMonitor"]["track"] is string track)
            {
                if (track == "everything")
                {
                    if (config.Root!.Element("NetworkSessionMonitor") is not XElement monitor)
                    {
                        config.Root.Add(monitor = new XElement("NetworkSessionMonitor"));
                    }

                    monitor.RemoveNodes();
                }
            }
            else if (config.Root!.Element("NetworkSessionMonitor") is XElement monitor)
            {
                monitor.Remove();
            }
        }

        void IInitialConfigurationReader.Apply(XDocument config, InitializationPrefs ini)
        {
            if (config.Root!.Element("NetworkSessionMonitor") is XElement monitor)
            {
                ini["NetworkSessionMonitor"]["track"] = monitor.Elements().Any() ? "custom" : "everything";
            }
        }
    }
}   
