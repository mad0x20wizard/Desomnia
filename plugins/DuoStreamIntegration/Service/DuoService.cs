using MadWizard.Desomnia.Service.Controller;
using Microsoft.Win32;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoService(string serviceName) : ObservableServiceController(serviceName)
    {
        const string REGISTRY_KEY = "SOFTWARE\\Duo";
        const ushort DEFAULT_PORT = 38299;

        public DuoSettings Settings
        {
            get
            {
                using var duo = Registry.LocalMachine!.OpenSubKey(REGISTRY_KEY) 
                    ?? throw new FileNotFoundException(fileName: REGISTRY_KEY, message: "Duo registry key not found");
                using var instances = duo.OpenSubKey("Instances")
                    ?? throw new FileNotFoundException(fileName: REGISTRY_KEY, message: "Duo instances key not found");

                return new DuoSettings
                {
                    Port = duo?["Port"] is int port ? (ushort)port : DEFAULT_PORT,

                    Instances = [.. ReadInstanceSettings(instances)]
                };
            }
        }

        private static IEnumerable<InstanceSettings> ReadInstanceSettings(RegistryKey instancesKey)
        {
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
