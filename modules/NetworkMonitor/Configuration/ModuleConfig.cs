using MadWizard.Desomnia.Network.Configuration.Interfaces;
using System.Collections.ObjectModel;

namespace MadWizard.Desomnia.Network.Configuration
{
    public class ModuleConfig<T> where T : NetworkMonitorConfig
    {
        /// <summary>
        /// The configured networks, in document order. The list stamps every network's
        /// <see cref="NetworkMonitorConfig.Ordinal"/> — the identity that correlates this
        /// view with the plugins' views of the same configuration (each binds its own list
        /// from the same sections, in the same order).
        /// </summary>
        public IList<T> NetworkMonitor { get; private set; } = new OrdinalList<T>();

        /// <summary>
        /// Root-level (environment-scoped) interface blocks. An IList of a complex type, so
        /// the collection-element derivation marks it — the environment merge then APPENDS
        /// nameless blocks instead of fusing them.
        /// </summary>
        public IList<NetworkInterfaceBlockInfo> NetworkInterfaceBlock { get; private set; } = [];

        private sealed class OrdinalList<TItem> : Collection<TItem> where TItem : NetworkMonitorConfig
        {
            protected override void InsertItem(int index, TItem item)
            {
                item.Ordinal = index;

                base.InsertItem(index, item);
            }
        }
    }
}
