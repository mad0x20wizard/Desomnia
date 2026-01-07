namespace MadWizard.Desomnia.Session
{
    public class SessionUsageToken(string userName, string? clientName = null) : UsageToken
    {
        public string UserName => userName;
        public string? ClientName => clientName;

        public bool IsRemote => clientName != null;

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

        public override string ToString() => "<" + (HasNetworkSession ? @"\\" : "") + (clientName != null ? @$"{clientName}\" : string.Empty) + userName + ">";
    }
}
