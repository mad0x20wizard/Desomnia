namespace MadWizard.Desomnia.Network.Interface.Configuration
{
    public abstract class Configurator(object target)
    {
        internal object Target => target;

        internal abstract object ReadRawConfiguration();
        internal abstract Task ApplyConfiguration(object configuration);
    }

    public abstract class Configurator<T>(T target) : Configurator(target) where T : notnull
    {
        internal protected abstract T   ReadConfiguration();
        internal sealed override object ReadRawConfiguration() => ReadConfiguration();

        internal protected abstract Task ApplyConfiguration(T config);
        internal sealed override    Task ApplyConfiguration(object config) => ApplyConfiguration((T)config);
    }
}
