using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Installer.Configuration
{
    internal class PowerRequestMonitorConfigurator : IInitialConfigurationBuilder, IInitialConfigurationReader
    {
        void IInitialConfigurationBuilder.Apply(InitializationPrefs ini, XDocument config)
        {
            if (ini["PowerRequestMonitor"]["track"] is string track)
            {
                if (track == "everything")
                {
                    if (config.Root!.Element("PowerRequestMonitor") is not XElement monitor)
                    {
                        config.Root.Add(monitor = new XElement("PowerRequestMonitor"));
                    }

                    monitor.RemoveNodes();
                }
            }
            else if (config.Root!.Element("PowerRequestMonitor") is XElement monitor)
            {
                monitor.Remove();
            }
        }

        void IInitialConfigurationReader.Apply(XDocument config, InitializationPrefs ini)
        {
            if (config.Root!.Element("PowerRequestMonitor") is XElement monitor)
            {
                ini["PowerRequestMonitor"]["track"] = monitor.Elements().Any() ? "custom" : "everything";
            }
        }
    }
}   
