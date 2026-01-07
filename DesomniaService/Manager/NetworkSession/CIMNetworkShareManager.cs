using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.NetworkSession.Manager
{
    internal class CIMNetworkShareManager(CIMNetworkFileManager fileManager) : CIMSMBManager<string, CIMNetworkShare>("MSFT_SmbShare"), INetworkShareManager
    {
        public required ILogger<CIMNetworkShareManager> Logger { private get; init; }

        public IEnumerator<INetworkShare> GetEnumerator() => Objects.Values.GetEnumerator();

        internal CIMNetworkShare FindShareByFile(CIMNetworkFile file)
        {
            return Objects.Values.Where(share => Path.Combine(share.Path, file.ShareRelativePath) == file.Path).First();
        }

        protected override ISet<string> RefreshObjects(Dictionary<string, CIMNetworkShare> objects)
        {
            var existingNames = new HashSet<string>();

            foreach (var instance in Instances)
            {
                var name = (String)instance.CimInstanceProperties["Name"].Value;
                var path = (String)instance.CimInstanceProperties["Path"].Value;

                existingNames.Add(name);

                if (!objects.TryGetValue(name, out var share))
                {
                    objects[name] = share = new CIMNetworkShare(name, path)
                    {
                        OpenFiles = fileManager.Where(file => file.Path == Path.Combine(path, file.ShareRelativePath))
                    };
                }

                share.UpdateProperties(instance);
            }

            return existingNames;
        }
    }
}