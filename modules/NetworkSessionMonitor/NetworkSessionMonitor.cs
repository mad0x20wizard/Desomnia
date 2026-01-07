using Autofac;
using MadWizard.Desomnia.NetworkSession.Configuration;
using MadWizard.Desomnia.NetworkSession.Manager;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.NetworkSession
{
    public class NetworkSessionMonitor(INetworkSessionManager manager) : IInspectable, IStartable
    {
        static readonly bool SHOW_SHARE_USAGE = false;

        public required ILogger<NetworkSessionMonitor> Logger { private get; init; }

        public required IEnumerable<NetworkSessionFilterRule> Rules { private get; init; }

        void IStartable.Start()
        {
            Logger.LogDebug($"Startup complete; {Rules.Count()} filter rules found.");
        }

        IEnumerable<UsageToken> IInspectable.Inspect(TimeSpan interval)
        {
            foreach (var session in manager)
            {
                if (session.IdleTime > interval)
                    continue;

                var filteredFiles = session.OpenFiles.Where(file => !ShouldFilterFile(file, Rules));

                if (filteredFiles.Any())
                {
                    if (SHOW_SHARE_USAGE)
                    {
                        var shares = new HashSet<INetworkShare>();
                        foreach (var file in filteredFiles)
                            shares.Add(file.Share);

                        foreach (var share in shares)
                            yield return new NetworkSessionUsage(session, share);
                    }
                    else
                    {
                        yield return new NetworkSessionUsage(session);
                    }
                }
            }
        }

        private static bool ShouldFilterFile(INetworkFile file, IEnumerable<NetworkSessionFilterRule> filters)
        {
            foreach (var rule in filters)
                if (ShouldFilterFile(file, rule))
                    return true;

            return false;
        }

        private static bool ShouldFilterFile(INetworkFile file, NetworkSessionFilterRule rule)
        {
            int match = 0, mismatch = 0;

            if (rule.UserName is string ruleUserName && file.Session.UserName is string userName)
            {
                var _ = string.Equals(ruleUserName, userName, StringComparison.InvariantCultureIgnoreCase) ? match++ : mismatch++;
            }

            if (rule.ClientName is string ruleClientName && file.Session.Client.Name is string clientName)
            {
                var _ = string.Equals(ruleClientName, clientName, StringComparison.InvariantCultureIgnoreCase) ? match++ : mismatch++;
            }

            if (rule.ClientIPAddress is IPAddress ruleIPAddress && file.Session.Client.Address is IPAddress address)
            {
                var _ = ruleIPAddress.Equals(address) ? match++ : mismatch++;
            }

            if (rule.ShareName is string ruleShareName && file.Share.Name is string shareName)
            {
                var _ = string.Equals(ruleShareName, shareName, StringComparison.InvariantCultureIgnoreCase) ? match++ : mismatch++;
            }

            if (rule.FilePathPattern is Regex pattern)
            {
                var _ = pattern.IsMatch(file.Path) ? match++ : mismatch++;
            }

            return rule.Type switch
            {
                FilterType.Must => mismatch != 0,
                FilterType.MustNot => match != 0,

                _ => throw new NotImplementedException("unknown filter rule type"),
            };
        }
    }
}
