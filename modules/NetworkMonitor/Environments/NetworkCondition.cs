using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Interface;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Environments
{
    /// <summary>
    /// Requires an available network connection, identified by its CIDR form
    /// (network="192.168.128.0/24") - satisfied when any operational interface
    /// has an address inside that network.
    /// </summary>
    internal sealed class NetworkCondition : NetworkChangeCondition
    {
        public NetworkCondition(InterfaceMatcher matcher, string value) : base(matcher)
        {
            try
            {
                matcher.WithNetwork(IPNetwork.Parse(value)).WithStatus(OperationalStatus.Up);
            }
            catch (FormatException ex)
            {
                throw new ConfigurationValueException($"Invalid network '{value}'; expected CIDR notation (e.g. \"192.168.1.0/24\").", ex);
            }
        }
    }
}
