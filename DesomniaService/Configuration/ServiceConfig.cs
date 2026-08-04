namespace MadWizard.Desomnia.Service.Configuration
{
    public class ServiceConfig
    {
        // internal rather than public: the type behind it is nobody's business outside this
        // assembly, and the binder reads non-public properties anyway
        internal ProcessManagerConfig ProcessManager { get; set; } = new();
    }
}
