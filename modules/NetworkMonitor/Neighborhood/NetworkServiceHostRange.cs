using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    internal class NetworkServiceHostRange : NetworkHostRange
    {
        public required IEnumerable<TransportFilterRule> Rules { internal get; init; }
    }
}
