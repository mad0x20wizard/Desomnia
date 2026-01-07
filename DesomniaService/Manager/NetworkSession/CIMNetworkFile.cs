using Microsoft.Management.Infrastructure;


namespace MadWizard.Desomnia.NetworkSession.Manager
{
    internal class CIMNetworkFile(ulong id, ulong sid, string path, string pathRelative) : INetworkFile
    {
        internal ulong Id => id;
        internal ulong SessionId => sid;

        public string Path => path;
        internal string ShareRelativePath => pathRelative;

        public INetworkShare Share => LookupShare(this);
        public INetworkSession Session => LookupSession(this);

        public uint Locks { get; private set; }

        internal required Func<CIMNetworkFile, CIMNetworkShare> LookupShare { private get; init; }
        internal required Func<CIMNetworkFile, CIMNetworkSession> LookupSession { private get; init; }

        internal void UpdateProperties(CimInstance instance)
        {
            if (instance.CimInstanceProperties["Locks"].Value is UInt32 locks)
                Locks = locks;
        }
    }
}
