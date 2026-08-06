using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public class SessionUsage(string userName, string? clientName = null) : UsageToken
    {
        public string   UserName    => userName;
        public string?  ClientName  => clientName;
        public bool     IsRemote    => clientName != null;

        public SessionUsage(ISession session) : this(session.UserName, session.ClientName) { }

        public ProcessUsageMetrics? Metrics { get; set; }

        public bool HasNetworkSession { get; set; }

        public override string ToString()
        {
            string str = string.Empty;

            str += "<";

            if (HasNetworkSession)
                str += @"\\";

            str += (clientName != null ? @$"{clientName}\" : string.Empty) + userName;

            if (Metrics is not null)
            {
                str += " @ " + Metrics.ToString();
            }

            if (Tokens.Any())
            {
                str += " -> " + string.Join(", ", Tokens.Select(x => x.ToString()));
            }

            str += ">";

            return str;
        }
    }
}
