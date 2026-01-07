using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    public interface INetworkFile
    {
        string Path { get; }

        INetworkShare Share { get; }
        INetworkSession Session { get; }

        uint Locks { get; }

        // void Close();
    }
}
