using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    public interface INetworkSession
    {
        string UserName { get; }

        INetworkClient Client { get; }

        IEnumerable<INetworkFile> OpenFiles { get; }

        public TimeSpan ConnectionTime { get; }
        public TimeSpan IdleTime { get; }

        // void Disconnect();
    }
}
