namespace MadWizard.Desomnia.PowerRequest.Configuration
{
    public class PowerRequestMonitorConfig
    {
        public IList<PowerRequestInfo> Request { get; set; } = [];
        public IList<PowerRequestInfo> RequestFilter { get; set; } = [];
    }
}
