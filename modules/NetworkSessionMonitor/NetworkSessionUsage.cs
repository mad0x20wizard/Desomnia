using MadWizard.Desomnia.NetworkSession.Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.NetworkSession
{
    public class NetworkSessionUsage(INetworkSession session, INetworkShare? share = null) : UsageToken
    {
        public string ClientName => session.Client.Name ?? session.Client.Address?.ToString() ?? "?";

        public string UserName => session.UserName;

        public override string ToString() => @$"\\{ClientName}\{UserName}{(share != null ? "@" + share.Name : "")}";
    }
}
