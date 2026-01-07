using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    public interface INetworkShare
    {
        string Name { get; }

        string Description { get; }

        string Path { get; }

        IEnumerable<INetworkFile> OpenFiles { get; }

    }
}
