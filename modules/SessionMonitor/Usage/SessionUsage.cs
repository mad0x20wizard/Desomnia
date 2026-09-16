using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public abstract class SessionUsage<T>(string userName, string? clientName = null) : MetricsUsageToken<T> where T : SessionMetricsUsage?
    {
        public string   UserName    => userName;
        public string?  ClientName  => clientName;
        public bool     IsRemote    => clientName != null;

        public bool HasNetworkSession { get; set; }

        public override string ToString()
        {
            string str = string.Empty;

            str += "<";

            if (HasNetworkSession)
                str += @"\\";

            str += (clientName != null ? @$"{clientName}\" : string.Empty) + userName;

            str += Metrics?.ToString();

            if (Tokens.Any())
            {
                str += " -> " + string.Join(", ", Tokens.Select(x => x.ToString()));
            }

            str += ">";

            return str;
        }
    }

    public class SessionUsage(string userName, string? clientName = null) : SessionUsage<SessionMetricsUsage>(userName, clientName)
    {
        public SessionUsage(ISession session) : this(session.UserName, session.ClientName) { }
    }
}
