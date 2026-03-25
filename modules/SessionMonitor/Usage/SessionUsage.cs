using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public class SessionUsage(string userName, string? clientName = null) : UsageToken
    {
        public string UserName => userName;
        public string? ClientName => clientName;

        public bool IsRemote => clientName != null;

        public SessionUsage(ISession session) : this(session.UserName, session.ClientName) { }

        //public bool MatchesNetworkSession(NetworkSessionUsage usage)
        //{
        //    if (clientName != null)
        //    {
        //        if (!clientName.Equals(usage.ClientName, StringComparison.InvariantCultureIgnoreCase))
        //            return false;

        //        if (!userName.Equals(usage.UserName, StringComparison.InvariantCultureIgnoreCase))
        //            return false;

        //        return true;
        //    }

        //    return false;
        //}

        public bool HasNetworkSession { get; set; }

        public override string ToString()
        {
            string str = string.Empty;

            str += "<";

            if (HasNetworkSession)
                str += @"\\";

            str += (clientName != null ? @$"{clientName}\" : string.Empty) + userName;

            foreach (var process in Tokens)
                str += "+" + process.ToString();

            str += ">";

            return str;

        }
    }
}
