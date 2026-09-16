namespace MadWizard.Desomnia.Configuration.Binding
{
    public class ConfigurationException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
    {

    }
}
