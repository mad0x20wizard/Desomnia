using MadWizard.Desomnia.Network.Manager;

namespace MadWizard.Desomnia.Network.Interface.Manager
{
    /// <summary>
    /// The identity guarantee behind <see cref="INetworkInterfaceManager"/>: a detached
    /// interface is remembered — weakly — for as long as anyone still references the
    /// instance, and a return with an equal <see cref="NetworkIdentity"/> recalls that very
    /// instance, so upper layers never need identity checks beyond reference equality. Once
    /// the last reference is collected the entry clears itself, and a later return gets a
    /// fresh instance.
    ///
    /// Not thread-safe on purpose — call under the manager's lock.
    /// </summary>
    internal class InterfaceMemory
    {
        private readonly List<WeakReference<NetworkInterfaceImpl>> _remembered = [];

        /// <summary>How many detachments are currently remembered, dead entries included —
        /// a seam for the manager's no-orphaned-entries regression test.</summary>
        public int Count => _remembered.Count;

        /// <summary>Remembers an interface that just detached.</summary>
        public void Remember(NetworkInterfaceImpl @interface)
        {
            Prune();

            _remembered.Add(new WeakReference<NetworkInterfaceImpl>(@interface));
        }

        /// <summary>
        /// Recalls — and forgets — a still-referenced interface with this identity, or null
        /// if none survived. Most-recently-remembered first: a re-enumeration pulse must
        /// recall the very instance it just remembered.
        /// </summary>
        public NetworkInterfaceImpl? Recall(NetworkIdentity identity)
        {
            Prune();

            for (int i = _remembered.Count - 1; i >= 0; i--)
            {
                if (_remembered[i].TryGetTarget(out NetworkInterfaceImpl? @interface) && @interface.Identity == identity)
                {
                    _remembered.RemoveAt(i);

                    return @interface;
                }
            }

            return null;
        }

        private void Prune()
        {
            _remembered.RemoveAll(reference => !reference.TryGetTarget(out _));
        }
    }
}
