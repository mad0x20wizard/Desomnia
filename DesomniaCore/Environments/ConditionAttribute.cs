namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// One condition attribute of an &lt;Environment&gt; block, as written. A plain attribute
    /// (<c>power="ac"</c>) resolves a named <see cref="IEnvironmentCondition"/> registration;
    /// a namespaced attribute (<c>env:USER="Kevin"</c>) resolves the
    /// <see cref="IEnvironmentConditionProvider"/> registered for its namespace URI, which
    /// receives the local name and value.
    /// </summary>
    /// <param name="Namespace">The XML namespace URI (e.g. "environment:process"), or null for a plain attribute.</param>
    /// <param name="Name">The attribute's local name.</param>
    /// <param name="Value">The attribute's value.</param>
    /// <param name="DisplayName">The attribute as written (e.g. "env:USER") - for logging and errors.</param>
    internal sealed record ConditionAttribute(string? Namespace, string Name, string Value, string DisplayName);
}
