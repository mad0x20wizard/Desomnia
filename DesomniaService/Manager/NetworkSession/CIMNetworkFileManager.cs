using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    internal class CIMNetworkFileManager() : CIMSMBManager<ulong, CIMNetworkFile>("MSFT_SmbOpenFile"), IIEnumerable<CIMNetworkFile>
    {
        public required ILogger<CIMNetworkFileManager> Logger { private get; init; }

        public CIMNetworkSessionManager? SessionManager { get; set; }
        public CIMNetworkShareManager? ShareManager { get; set; }

        public IEnumerator<CIMNetworkFile> GetEnumerator() => Objects.Values.GetEnumerator();

        protected override ISet<ulong> RefreshObjects(Dictionary<ulong, CIMNetworkFile> objects)
        {
            var existingIDs = new HashSet<ulong>();

            foreach (var instance in Instances)
            {
                var id = (UInt64)instance.CimInstanceProperties["FileId"].Value;
                var sid = (UInt64)instance.CimInstanceProperties["SessionId"].Value;

                var path = ((String)instance.CimInstanceProperties["Path"].Value).TrimEnd('\\');
                var pathRelative = ((String)instance.CimInstanceProperties["ShareRelativePath"].Value).TrimEnd('\\');

                if (path.StartsWith(@"\"))
                    continue; // skip if path is a UNC path

                existingIDs.Add(id);

                if (!objects.TryGetValue(id, out var file))
                {
                    objects[id] = file = new CIMNetworkFile(id, sid, path, pathRelative)
                    {
                        LookupSession = file => SessionManager![file.SessionId],
                        LookupShare = ShareManager!.FindShareByFile,
                    };
                }

                file.UpdateProperties(instance);
            }

            return existingIDs;
        }
    }
}
