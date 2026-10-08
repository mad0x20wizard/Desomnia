Interface configuration
=======================

Use ``<NetworkInterface>`` at the root of ``<SystemMonitor>`` to configure an
adapter independently of network monitoring:

.. code:: xml

    <SystemMonitor>
        <NetworkInterface name="Ethernet" disabled="false" />
        <NetworkInterface name="Wi-Fi" disabled="true" />
    </SystemMonitor>

``name`` is an :doc:`interface selector <selection>`. It accepts the same
patterns as a network monitor's ``interface`` attribute. One selector can
match several adapters; each present adapter has one interface watch.
Declarations inside ``<NetworkMonitor>`` are ignored.

Administrative state
--------------------

``disabled="true"`` disables matching interfaces; ``disabled="false"`` enables
them. This is administrative state: an enabled adapter with an unplugged cable
is not administratively disabled.

Omitting ``disabled`` leaves that property unspecified. If no matching declaration
supplies it, Desomnia releases any override left by the previous environment and
restores the latest externally observed state. Otherwise, omission inherits an
earlier matching declaration's value.

Desomnia applies interface settings before selecting interfaces for network
monitoring. It checks them again on network changes. Interfaces that are actually
enabled and operational can be monitored, including ones manually enabled when
external changes are allowed.

Excluding interfaces from monitoring
-----------------------------------

.. code:: xml

    <NetworkInterface name="Wi-Fi" monitor="false" />

``monitor="false"`` excludes matching adapters from network monitoring without
changing their OS state. Their interface watches remain active, so configured
``disabled`` settings and restoration still apply. Manually enabling an adapter
does not override this monitoring exclusion.

An omitted ``monitor`` inherits an earlier matching declaration's value; the
effective default is ``true``. A later explicit ``monitor="true"`` enables
monitoring eligibility again. A matching ``NetworkMonitor`` configuration is
still required to create a network context.

Allowing external changes
-------------------------

.. code:: xml

    <NetworkInterface name="Wi-Fi" disabled="true" allowToChange="disabled" />

By default, configured properties are enforced. ``allowToChange`` is a flags
value naming the properties external software or the user may change. Currently
``disabled`` is the only change flag; ``none`` selects strict enforcement.

With ``allowToChange="disabled"``, Desomnia still applies the initial configured
state, but accepts subsequent external changes. Every environment rebuild applies
the effective configuration again. A genuine removal and reconnection also creates
a fresh watch; disabling an adapter does not count as physical removal.

Ordering and environments
-------------------------

Environment merging combines declarations with the same ``name`` according to
:doc:`environment priority </concepts/environments/merging>` and conflict rules.
Differently named selectors that match the same adapter are applied in effective
configuration order: later explicitly supplied properties override earlier ones.
An omitted property does not clear an inherited value.

For example, use an environment condition to disable WiFi while Ethernet is up:

.. code:: xml

    <Environment interface="eth0@up">
        <SystemMonitor>
            <NetworkInterface name="wlan0" disabled="true" />
        </SystemMonitor>
    </Environment>

See the :doc:`network environment conditions </concepts/environments/conditions/network>`.
Do not make an adapter's availability the condition for disabling that same adapter,
since this can repeatedly activate and deactivate the environment.

Restoration
-----------

The persistent interface manager retains restoration information across environment
rebuilds. Ending an individual monitor or replacing the ephemeral application does
not release interface settings; the replacement reconciles them with its configuration.

Observed external administrative-state changes become the new restoration baseline
and clear Desomnia's pushed state. Strict enforcement may subsequently reapply the
configured state. Releasing that override, or exiting Desomnia, restores the latest
external baseline. Without an external change, the original administrative state is
restored. A physically removed device has no state to restore; its next attachment
starts with a fresh baseline.

Platform requirements
---------------------

Interface configuration requires the privileges of Desomnia's system service.
Windows uses CIM and includes disabled adapters in discovery. Linux uses ``ip``
from iproute2 to change state and reads administrative flags from sysfs. macOS uses
``ifconfig``. Enabling an adapter does not guarantee an immediate connection or IP address.
