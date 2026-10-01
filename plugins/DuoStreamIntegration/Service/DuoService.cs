using MadWizard.Desomnia.Service.Controller;
using Microsoft.Win32;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoService(string serviceName) : ObservableServiceController(serviceName)
    {
        internal const string REG_Duo = "SOFTWARE\\Duo";
        internal const string REG_DuoInstances = REG_Duo + "\\Instances";

        internal const ushort DefaultPort = 38299;

        public DuoSettings Settings
        {
            get
            {
                using var duo = Registry.LocalMachine!.OpenSubKey(REG_Duo) 
                    ?? throw new FileNotFoundException(fileName: REG_Duo, message: "Duo registry key not found");

                return new DuoSettings
                {
                    Port = duo?["Port"] is int port ? (ushort)port : DefaultPort,

                    Instances = [.. ReadInstanceSettings()]
                };
            }
        }

        private static IEnumerable<InstanceSettings> ReadInstanceSettings()
        {
            using var instancesKey = Registry.LocalMachine!.OpenSubKey(REG_DuoInstances)
                ?? throw new FileNotFoundException(fileName: REG_DuoInstances, message: "Duo instances key not found");

            foreach (var name in instancesKey.GetSubKeyNames().OfType<string>())
            {
                using var key = instancesKey.OpenSubKey(name);

                if (key != null)
                {
                    yield return new InstanceSettings
                    {
                        Name = name,
                        DisplayName = key["DisplayName"] is string displayName ? displayName : throw new ArgumentNullException("DisplayName"),
                        Port = key["Port"] is int port ? (ushort)port : throw new ArgumentNullException("Port"),
                        UserName = key["UserName"] is string userName ? userName : throw new ArgumentNullException("UserName"),
                        IsSandboxed = key["Sandboxed"] is int sandboxed && sandboxed == 1
                    };
                }
            }
        }
    }
}
