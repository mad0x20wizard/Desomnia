using Microsoft.Management.Infrastructure;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    internal class CIMNetworkShare(string name, string path) : INetworkShare
    {
        public string Name => name;
        public string Path => path;

        public string Description { get; internal set; } = "";

        public required IEnumerable<INetworkFile> OpenFiles { get; internal init; }

        internal void UpdateProperties(CimInstance instance)
        {
            if (instance.CimInstanceProperties["Description"].Value is String description)
                Description = description;
        }
    }
}
