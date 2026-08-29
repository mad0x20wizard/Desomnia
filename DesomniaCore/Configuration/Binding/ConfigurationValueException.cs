namespace MadWizard.Desomnia.Configuration.Binding
{
    /// <summary>
    /// Thrown when a configuration value exists but cannot be converted to the type of the
    /// property, constructor parameter or collection item it is bound to.
    ///
    /// Unlike the stock <c>Microsoft.Extensions.Configuration.Binder</c>, the
    /// <see cref="StrictConfigurationBinder"/> propagates this exception out of collection
    /// and dictionary binding instead of silently swallowing it, so a typo in a single
    /// attribute aborts startup instead of dropping the whole element without a trace.
    ///
    /// Derives from <see cref="InvalidOperationException"/> to stay compatible with code
    /// that catches the stock binder's conversion errors.
    /// </summary>
    public class ConfigurationValueException(string message, Exception? innerException = null) : ConfigurationException(message, innerException)
    {

    }
}
