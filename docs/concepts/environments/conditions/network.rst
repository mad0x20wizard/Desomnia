Network
=======

Network conditions select settings according to interface presence, connection
state, IP addresses, or wireless network names.

network
-------

A subnet in CIDR notation, for example ``network="192.168.178.0/24"``.
Matches when an operational interface has an address inside that subnet.
IPv4 and IPv6 are supported.

.. rubric:: Example

This configuration enables a :doc:`process monitor </modules/process/monitor>`
for a backup application only while the machine is connected to the home network.
The backup process's storage activity then contributes to keeping the machine
awake:

.. code:: xml

   <EnvironmentMonitor version="3">
     <DefaultEnvironment>
       <SystemMonitor timeout="5min" onUsage="sleepless" onIdle="sleep">
         <PowerRequestMonitor />
       </SystemMonitor>
     </DefaultEnvironment>

     <Environment network="192.168.178.0/24">
       <ProcessMonitor>
         <Process name="Backup" minIO="100kb/s">backup</Process>
       </ProcessMonitor>
     </Environment>
   </EnvironmentMonitor>

Replace the subnet and ``backup`` process expression with the local values.
Outside this network, the process monitor is absent from the effective
configuration; the default power-request monitoring remains active.

An environment that only adds monitors can omit its inner ``SystemMonitor``.
The explicit element is required for system settings such as ``timeout``.

interface
---------

An :doc:`interface selector </modules/network/interface>`, optionally followed
by an operational status: ``interface="en0@up"``. Without a status, matches
any selected adapter that is present, including disconnected adapters.

Accepted statuses can be combined with ``|``, for example
``interface="en0@up|dormant"``. Status names are case-insensitive and describe
the operational state reported by the operating system:

``up``
    The interface is operational and capable of transmitting packets. This does
    not establish that the internet or a particular remote service is reachable.

``down``
    The interface cannot transmit packets, for example because it is disabled
    or its link is disconnected.

The status suffix applies to environment conditions only. It is not supported
by the :ref:`NetworkMonitor interface <network-monitor-interface>` attribute.

.. rubric:: Example

The following fragment blocks WiFi while the wired interface is operational,
without requiring a ``NetworkMonitor`` for the wired connection:

.. code:: xml

   <Environment interface="eth0@up">
     <SystemMonitor>
       <NetworkInterface name="wlan0" disabled="true" />
     </SystemMonitor>
   </Environment>

The interface names must match the local system. Windows accepts an exact
adapter display name, for example ``Ethernet``; Linux and macOS use interface
names such as ``eth0`` or ``en5``.

The :doc:`interface configuration </modules/network/blocking>` is removed when
the wired interface is no longer operational, allowing WiFi to be restored.
A blocked interface must not also be the condition that activates its own block;
that dependency can cause repeated configuration changes.

ssid
----

:OS: 🪟 *Windows*

The exact wireless network name, for example ``ssid="Home WiFi"``.
The value is case-sensitive and is not a regular expression. Linux and macOS
do not currently supply SSID information to this condition.

.. rubric:: Example

This fragment selects settings by the exact wireless network name:

.. code:: xml

   <Environment ssid="Home WiFi">
     <SystemMonitor timeout="5min" />
   </Environment>
