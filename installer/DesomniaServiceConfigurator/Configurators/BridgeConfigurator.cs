using System.Xml.Linq;

namespace MadWizard.Desomnia.Service.Installer.Configuration
{
    internal class BridgeConfigurator : IInitialConfigurationBuilder, IInitialConfigurationReader
    {
        public void Apply(InitializationPrefs ini, XDocument config)
        {
            if (config.Root!.Element("SessionMonitor") is XElement monitor)
                if (ini["SessionMonitor"]["allowSleepControl"] is string control)
                {
                    if (control != "custom")
                    {
                        RemoveSleepControl(monitor);

                        XElement? session = null;

                        switch (control)
                        {
                            case "user":
                                var name = Environment.UserName;

                                foreach (var user in monitor.Elements("User"))
                                    if (user.Attribute("name") is XAttribute attr && attr.Value == name)
                                        session = user;

                                if (session == null)
                                {
                                    monitor.Add(session = new XElement("User", new XAttribute("name", name)));
                                }

                                break;

                            case "everyone":
                                session = GetOrCreateElement(monitor, "Everyone");
                                break;

                            case "administrator":
                                session = GetOrCreateElement(monitor, "Administrator");
                                break;
                        }

                        session?.Add(new XAttribute("allowControlSleep", "true"));
                    }
                }
                else
                {
                    RemoveSleepControl(monitor);
                }
        }

        void IInitialConfigurationReader.Apply(XDocument config, InitializationPrefs ini)
        {
            List<XElement> sessions = [];

            if (config.Root!.Element("SessionMonitor") is XElement monitor)
                foreach (var element in monitor.Elements())
                    if (element.Attribute("allowControlSleep") is XAttribute attr && attr.Value == "true")
                        sessions.Add(element);

            if (sessions.Count > 1)
            {
                ini["SessionMonitor"]["allowSleepControl"] = "custom";
            }
            else if (sessions.FirstOrDefault() is XElement session)
            {
                ini["SessionMonitor"]["allowSleepControl"] = session.Name.LocalName switch
                {
                    "User"  => session.Attribute("name")?.Value == Environment.UserName ? "user" : "custom",
                    "Everyone" => "everyone",
                    "Administrator" => "administrator",

                    _ => "custom"
                };
            }
        }

        private static void RemoveSleepControl(XElement monitor)
        {
            foreach (var session in monitor.Elements())
                session.Attribute("allowControlSleep")?.Remove();
        }

        private static XElement GetOrCreateElement(XElement parent, string name)
        {
            XElement? element = parent.Element(name);

            if (element == null)
            {
                parent.Add(element = new XElement(name));
            }

            return element;
        }
    }
}
