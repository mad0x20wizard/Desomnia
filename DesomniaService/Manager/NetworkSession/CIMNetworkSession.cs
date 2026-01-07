using Microsoft.Management.Infrastructure;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    internal class CIMNetworkSession(ulong id) : INetworkSession
    {
        internal ulong Id => id;

        public string UserName { get; private set; } = "?";

        public required INetworkClient Client { get; internal init; }
        public required IEnumerable<INetworkFile> OpenFiles { get; internal init; }

        public TimeSpan ConnectionTime { get; private set; }
        public TimeSpan IdleTime { get; private set; }

        internal void UpdateProperties(CimInstance instance)
        {
            UserName = (instance.CimInstanceProperties["ClientUserName"].Value as String)?.Split(@"\") is string[] split ? split[^1] : "?";

            if (instance.CimInstanceProperties["SecondsExists"].Value is UInt32 secondsExists)
                ConnectionTime = TimeSpan.FromSeconds(secondsExists);
            if (instance.CimInstanceProperties["SecondsIdle"].Value is UInt32 secondsIdle)
                IdleTime = TimeSpan.FromSeconds(secondsIdle);
        }
    }
}
