using Microsoft.Management.Infrastructure;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Windows implementation of the interface manager. Disabling and enabling go through CIM
    /// (MSFT_NetAdapter.Disable/Enable — the operation behind Disable-/Enable-NetAdapter),
    /// with the adapter looked up by interface GUID. That lookup also finds currently
    /// DISABLED adapters — which the BCL enumeration does not: on Windows our own disable
    /// removes the adapter from <see cref="NetworkInterface.GetAllNetworkInterfaces"/>, so
    /// <see cref="StillExists"/> has to ask CIM to tell a merely disabled adapter apart from
    /// one that is truly gone.
    ///
    /// It is also the platform that lets a service ask which wireless network an adapter is
    /// joined to (see <see cref="WiFiInterop"/>), so this is where the SSID answer exists.
    /// </summary>
    internal sealed class CIMNetworkInterfaceManager : NetworkInterfaceManager
    {
        private const string AdapterNamespace = @"root\StandardCimv2";

        protected override void DisableInterface(INetworkInterface @interface)
        {
            InvokeOnAdapter(@interface.Identity.Id, "Disable");
        }

        protected override void EnableInterface(INetworkInterface @interface)
        {
            InvokeOnAdapter(@interface.Identity.Id, "Enable");
        }

        /// <summary>
        /// Asks CIM instead of the enumeration the base consults: a disabled adapter is absent
        /// from the BCL enumeration but still present as an MSFT_NetAdapter instance, and it is
        /// exactly the adapter we disabled ourselves that must not be mistaken for a vanished
        /// one — its restore depends on this answer.
        /// </summary>
        protected override bool StillExists(INetworkInterface @interface)
        {
            try
            {
                using var session = CimSession.Create(null);

                using var adapter = FindAdapter(session, @interface.Identity.Id);

                return adapter is not null;
            }
            catch
            {
                return true; // if CIM will not tell, attempt the restore and let it speak
            }
        }

        /// <summary>
        /// Reads the CIM admin status, which — unlike the BCL <see cref="OperationalStatus"/> —
        /// tells an adapter we (or the user) disabled apart from one that is merely
        /// disconnected: both enumerate as Down, but only the disabled one has its admin status
        /// down. InterfaceAdminStatus follows IF-MIB ifAdminStatus (1 = up/enabled, 2 = down).
        /// </summary>
        protected override bool IsInterfaceDisabled(INetworkInterface @interface)
        {
            try
            {
                using var session = CimSession.Create(null);

                using var adapter = FindAdapter(session, @interface.Identity.Id);

                if (adapter?.CimInstanceProperties["InterfaceAdminStatus"]?.Value is { } adminStatus)
                    return Convert.ToInt32(adminStatus) != 1; // anything but "up" is administratively down
            }
            catch
            {
                // fall through — if CIM will not tell, assume enabled so a release still restores
            }

            return false;
        }

        protected override string? GetSSID(INetworkInterface @interface)
        {
            // only a wireless adapter can be joined to anything - checking the type first spares
            // the WLAN service a round trip for every other interface on the machine
            if (@interface.Type != NetworkInterfaceType.Wireless80211)
                return null;

            if (!Guid.TryParse(@interface.Identity.Id, out Guid interfaceId))
                return null;

            return WiFiInterop.GetCurrentSSID(interfaceId);
        }

        private static void InvokeOnAdapter(string interfaceId, string methodName)
        {
            using var session = CimSession.Create(null);

            using var adapter = FindAdapter(session, interfaceId)
                ?? throw new InvalidOperationException($"No network adapter with interface id '{interfaceId}' found.");

            session.InvokeMethod(AdapterNamespace, adapter, methodName, null)?.Dispose();
        }

        private static CimInstance? FindAdapter(CimSession session, string interfaceId)
        {
            string guid = interfaceId.Trim('{', '}');

            foreach (var instance in session.EnumerateInstances(AdapterNamespace, "MSFT_NetAdapter"))
            {
                if (instance.CimInstanceProperties["InterfaceGuid"]?.Value is string interfaceGuid
                    && string.Equals(interfaceGuid.Trim('{', '}'), guid, StringComparison.OrdinalIgnoreCase))
                {
                    return instance;
                }

                instance.Dispose();
            }

            return null;
        }
    }
}
