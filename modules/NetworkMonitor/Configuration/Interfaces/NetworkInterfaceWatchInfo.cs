namespace MadWizard.Desomnia.Network.Configuration.Interfaces
{
    [Flags]
    public enum NetworkInterfaceState
    {
        None = 0,
        Disabled = 1,
    }

    /// <summary>A root-level interface selector and its declarative settings.</summary>
    public class NetworkInterfaceWatchInfo
    {
        public required string Name { get; set; }

        public bool?    Disabled    { get; set; }
        public bool?    Monitor     { get; set; }

        // Nullable so omission can inherit from an earlier matching selector.
        public NetworkInterfaceState? AllowToChange { get; set; }
    }
}