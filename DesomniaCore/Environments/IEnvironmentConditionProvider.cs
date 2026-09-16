using Autofac;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Creates <see cref="IEnvironmentCondition"/>s for a whole XML namespace of condition
    /// attributes. While a plain condition attribute (<c>power="ac"</c>) resolves a
    /// <c>Named&lt;IEnvironmentCondition&gt;</c> registration per attribute name, a namespaced
    /// attribute (<c>env:USER="Kevin"</c> with <c>xmlns:env="environment:process"</c>)
    /// resolves the provider registered for the namespace URI —
    /// <c>Named&lt;IEnvironmentConditionProvider&gt;("environment:process")</c> — which
    /// receives the attribute's local name AND value, so one provider can serve an open set
    /// of keys. Providers register in the persistent container (see
    /// <see cref="Module.LoadOnce(ContainerBuilder)"/>), like conditions do; modules and
    /// plugins can contribute their own namespaces.
    /// </summary>
    public interface IEnvironmentConditionProvider
    {
        /// <summary>
        /// Creates the condition for one namespaced attribute. Implementations report an
        /// unusable name or value by throwing a
        /// <see cref="Configuration.Binding.ConfigurationValueException"/>. The created
        /// conditions may raise <see cref="IEnvironmentCondition.Changed"/> if the underlying
        /// state can change at runtime.
        /// </summary>
        /// <param name="name">The attribute's local name (e.g. "USER").</param>
        /// <param name="value">The attribute's value.</param>
        IEnvironmentCondition CreateCondition(string name, string value);
    }
}
