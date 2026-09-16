using MadWizard.Desomnia.Configuration.Model;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// The <see cref="EnvironmentMonitor"/>'s own configuration source: a drop-in
    /// replacement for a physical file source, serving the current effective configuration
    /// with full reload support — its providers raise their reload token whenever the
    /// monitor publishes a new effective configuration (a file edit or a condition change),
    /// so even <c>IOptionsMonitor</c>-style hot reloading works, although in practice the
    /// application loop rebuilds the whole inner host on the same signal.
    /// </summary>
    internal sealed class EnvironmentConfigurationSource(EnvironmentMonitor monitor) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new EnvironmentConfigurationProvider(monitor);
    }

    internal sealed class EnvironmentConfigurationProvider : ConfigurationProvider, IDisposable
    {
        readonly EnvironmentMonitor _monitor;

        OrderedConfigurationData _data = OrderedConfigurationData.Empty;

        public EnvironmentConfigurationProvider(EnvironmentMonitor monitor)
        {
            _monitor = monitor;

            // each inner host builds (and disposes) its own provider over the one monitor
            _monitor.EffectiveChanged += OnEffectiveChanged;
        }

        public override void Load()
        {
            _data = _monitor.Data;

            Data = _data.Data;
        }

        private void OnEffectiveChanged(EffectiveConfiguration effective)
        {
            _data = effective.Data;

            Data = _data.Data;

            OnReload(); // drives IOptionsMonitor and friends
        }

        /// <summary>Child keys in document order, like the file-backed provider.</summary>
        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
            => _data.GetChildKeys(earlierKeys, parentPath);

        public void Dispose() => _monitor.EffectiveChanged -= OnEffectiveChanged;
    }
}
