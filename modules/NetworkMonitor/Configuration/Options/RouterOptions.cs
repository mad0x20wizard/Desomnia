namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public struct RouterOptions
    {
        public RouterOptions()
        {

        }

        public bool AllowWake { get; set; } = false;
        public bool AllowWakeByProxy { get; set; } = false;
        public bool AllowWakeOnLAN { get; set; } = true;
        public bool AllowWakeByVPNClients { get; set; } = false;

        public TimeSpan VPNTimeout { get; set; }
    }
}
