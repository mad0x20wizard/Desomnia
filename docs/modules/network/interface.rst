Interface selection
===================

Desomnia can be configured to monitor one or more network interfaces installed in your system. Upon service startup and every time your network configuration changes, the configurations of all connected interfaces are compared against your ``<NetworkMonitor>`` configurations. There are several ways to control which configuration applies to which interface:

Automatic selection
-------------------

.. code:: xml

    <NetworkMonitor>
        <!-- hosts, etc. -->
    </NetworkMonitor>

If you specify neither ``interface`` nor ``network``, Desomnia monitors all interfaces that have a standard gateway configured. This is typically the interface connected to your local network. If multiple interfaces with a gateway are present — for example, an active wired and wireless connection to the same network — all of them will be monitored using this single configuration, which may be exactly what you want in that situation.

By network
----------

.. code:: xml

    <NetworkMonitor network="192.168.178.0/24">
        <!-- hosts, etc. -->
    </NetworkMonitor>

When you specify a network in CIDR notation, the ``<NetworkMonitor>`` is activated only for interfaces that have joined that particular network, as determined by the interface's IP address and prefix length. This applies to both IPv4 and IPv6 networks. You can also use a single concrete IP address to bind to a specific link.

By interface name
-----------------

.. code:: xml

    <NetworkMonitor interface="eth0">
        <!-- hosts, etc. -->
    </NetworkMonitor>

When you specify an interface name, the ``<NetworkMonitor>`` is activated only for interfaces with a matching name. The value is matched both by exact name and by whether the interface ID contains the specified string. How interfaces are identified differs between operating systems:

Windows
+++++++

:OS: 🪟

On Windows, every network interface is assigned a unique GUID and a human-readable name. The name can be changed in the system settings, but the GUID always remains the same. You can query both using PowerShell:

.. code:: PowerShell

    Get-NetAdapter | Select-Object Name, InterfaceDescription, InterfaceGuid

::

    Name                         InterfaceDescription                                      InterfaceGuid
    ----                         --------------------                                      -------------
    Bluetooth-Netzwerkverbindung Bluetooth Device (Personal Area Network)                  {6912B25F-0702-4ACC-AB22-82B3157A89FB}
    Netzwerkbrücke               Hyper-V Virtual Ethernet Adapter                          {5334C77C-2E13-4005-A7CE-C6889A312B5F}
    WLAN                         MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz Wireless LAN Card {44ECA482-24F2-4362-99F8-88A17A450B45}
    Ethernet                     Intel(R) Ethernet Controller (3) I225-V                   {1BD73899-523C-4911-967A-FE797ACF6C44}

To match by name, use the exact string shown in the ``Name`` column. To match by GUID, the curly braces are optional.

Linux and macOS
+++++++++++++++

:OS: 🐧 🍎

On Linux and macOS, interfaces are identified by their device name — typically something like ``eth0``, ``eth1``, ``en0``, or ``wlan0``, depending on the OS, distribution, and number of installed interfaces.

To list all interfaces and their names, use ``ip link show`` on Linux:

.. code:: bash

    ip link show

On older systems or macOS, ``ifconfig`` shows the same information:

.. code:: bash

    ifconfig

By interface name and network
-----------------------------

.. code:: xml

    <NetworkMonitor interface="eth0" network="192.168.178.20">
        <!-- hosts, etc. -->
    </NetworkMonitor>

You can combine both attributes to activate a configuration only when a specific interface has joined a specific network. If multiple configurations match the same interface, the first matching one in the configuration file is used. Each interface is monitored at most once.

Hot plugging
------------

You can add, remove, connect, or disconnect network interfaces at runtime without restarting Desomnia.
