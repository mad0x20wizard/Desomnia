using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Network.Configuration.Migration
{
    internal static class V3
    {
        internal static void Run(XDocument configuration)
        {
            foreach (var block in configuration.DescendantsNamed("NetworkInterfaceBlock").ToArray())
            {
                if (RemoveIfMonitorBlock(block))
                    continue;

                if (block.AttributeNamed("force") is XAttribute force)
                {
                    if (!bool.TryParse(force.Value, out bool enforce))
                        throw new ConfigurationValueException("NetworkInterfaceBlock force must be true or false.");

                    if (!enforce)
                    {
                        block.MigrateAdd(new XAttribute("allowToChange", "disabled"));
                    }

                    force?.MigrateRemove();
                }

                if (block.AttributeNamed("interface") is not XAttribute selector)
                    throw new ConfigurationValueException("A legacy NetworkInterfaceBlock needs an interface selector.");

                block.MigrateRename("NetworkInterface");
                selector.MigrateRename("name");
                block.MigrateAdd(new XAttribute("disabled", "true"));
            }
        }

        private static bool RemoveIfMonitorBlock(XElement block)
        {
            if (!string.Equals(block.Parent?.Name.LocalName, "SystemMonitor", StringComparison.OrdinalIgnoreCase))
            {
                block.MigrateRemove(reason: "Monitor-level interface blocks are no longer supported.");

                return true;
            }

            return false;
        }
    }
}
