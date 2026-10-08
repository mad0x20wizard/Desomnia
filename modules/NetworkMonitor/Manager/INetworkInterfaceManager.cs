namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Platform view of this machine's network interfaces.
    ///
    /// Enumeration includes all present interfaces, including administratively disabled
    /// adapters. Platform discovery must distinguish these from physically removed devices.
    ///
    /// Identity guarantee: the manager remembers a detached interface for as long as anyone
    /// still references the instance (weakly), and a return of the same
    /// <see cref="NetworkIdentity"/> resurfaces THE SAME instance through
    /// <see cref="InterfaceAttached"/>. Upper layers can rely on reference equality alone;
    /// only once the last reference is collected does a return produce a new instance.
    /// </summary>
    public interface INetworkInterfaceManager : IIEnumerable<INetworkInterface>
    {
        /// <summary>The present interface with this identity — null when none is currently in
        /// the platform inventory.</summary>
        INetworkInterface? this[NetworkIdentity identity] { get; }

        event EventHandler<INetworkInterface> InterfaceAttached;
        event EventHandler<INetworkInterface> InterfaceDetached;

        /// <summary>Any observed change (attach/detach/address/status) — a coarse re-plan trigger.</summary>
        event EventHandler? Changed;

        /// <summary>Refresh observations without raising Changed. Used before reconciliation and recovery polling.</summary>
        void Refresh() { }
    }
}
