using Autofac;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Environments.Export
{
    /// <summary>
    /// Writes a representation of the effective configuration somewhere a human can inspect
    /// it. Exporters are loosely coupled: they live in the persistent container as startables,
    /// subscribe to the <see cref="EnvironmentMonitor"/> when started (<see cref="Start"/> -
    /// after the required properties are injected, which a constructor-time subscription
    /// would run ahead of) and receive the effective configuration current at that moment,
    /// then every change; they clean their artifacts up when the application stops (disposal).
    /// An exporter must never throw out of <see cref="Export"/> — a failed export is a
    /// logged inconvenience, not a configuration failure.
    /// </summary>
    internal abstract class EffectiveConfigurationExporter : IStartable, IDisposable
    {
        public required ILogger Logger { protected get; init; }

        readonly EnvironmentMonitor _monitor;

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
            ArgumentNullException.ThrowIfNull(monitor);

            _monitor = monitor;
        }

        /// <summary>Subscribes to the monitor - and exports the effective configuration it has
        /// already published, if any: the pipeline (a startable, too) may well have run first.</summary>
        public void Start() => _monitor.Subscribe(Export);

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
            _monitor.EffectiveChanged -= Export;

            lock (this)
            {
                Remove();
            }
        }
    }
}
