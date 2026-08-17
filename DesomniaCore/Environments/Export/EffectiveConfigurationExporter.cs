using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Environments.Export
{
    /// <summary>
    /// Writes a representation of the effective configuration somewhere a human can inspect
    /// it. Exporters are loosely coupled: they live in the persistent container, react to
    /// <see cref="EnvironmentMonitor.EffectiveChanged"/> (the ApplicationBuilder wires the
    /// subscription), and clean their artifacts up when the application stops (disposal).
    /// An exporter must never throw out of <see cref="Export"/> — a failed export is a
    /// logged inconvenience, not a configuration failure.
    /// </summary>
    internal abstract class EffectiveConfigurationExporter : IDisposable
    {
        public required ILogger Logger { protected get; init; }

        protected string? WrittenPath
        {
            get;

            set
            {
                if (value != null)
                {
                    if (field != null)
                    {
                        if (!string.Equals(field, value, StringComparison.OrdinalIgnoreCase))
                            Remove();
                    }

                    Logger.LogTrace($"Effective configuration written to: '{value}'");
                }

                field = value;
            }
        }

        protected EffectiveConfigurationExporter(EnvironmentMonitor monitor)
        {
            monitor.EffectiveChanged += Export;
        }

        protected abstract void Export(EffectiveConfiguration effective);

        private void Remove()
        {
            if (WrittenPath is string path)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.LogWarning(ex, $"Failed to remove the effective configuration at {path}");
                }
                finally
                {
                    WrittenPath = null;
                }
            }
        }

        void IDisposable.Dispose()
        {
            lock (this)
            {
                Remove();
            }
        }
    }
}
