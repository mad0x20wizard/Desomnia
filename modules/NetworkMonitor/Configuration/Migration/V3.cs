using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Logging;
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

                block.MigrateRename("NetworkInterface");

                if (block.AttributeNamed("force") is var force && false is bool enforce)
                {
                    if (force is not null && !bool.TryParse(force.Value, out enforce))
                        throw new ConfigurationValueException("NetworkInterfaceBlock @force must be true or false.");

                    if (!enforce)
                    {
                        block.MigrateAdd(new XAttribute("allowToChange", "disabled"));
                    }

                    force?.MigrateRemove(level: LogLevel.Information);
                }

                if (block.AttributeNamed("interface") is not XAttribute selector)
                    throw new ConfigurationValueException("A legacy NetworkInterfaceBlock needs an interface selector.");

                block.MigrateAdd(new XAttribute("disabled", "true"));

                selector.MigrateRename("name");
            }
        }

        private static bool RemoveIfMonitorBlock(XElement block)
        {
            if (!string.Equals(block.Parent?.Name.LocalName, "SystemMonitor", StringComparison.OrdinalIgnoreCase))
            {
                block.MigrateRemove(reason: "<NetworkMonitor> interface blocks are no longer supported.");

                return true;
            }

            return false;
        }
    }
}
