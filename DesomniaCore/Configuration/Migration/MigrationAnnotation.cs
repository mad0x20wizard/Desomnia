using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// One report of a migration step: the path of the affected node in the document (as it
    /// was when the note was taken — a format-specific rendering, XPath for XML), the severity
    /// and the message. Internal on purpose: a module reports through the format's helpers
    /// (XML: <c>Configuration.Xml.XMigrationExtensions</c>), which capture the path themselves
    /// — and every change made past the helpers is reported by the format's change tracking.
    /// </summary>
    internal sealed record MigrationAnnotation(string Path, LogLevel Level, string Message);
}
