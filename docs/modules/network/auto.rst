Auto-configuration
==================

Statically mapping every IP address in your configuration works, but it requires manual updates whenever addresses change. If your router manages host-to-address mappings and exposes them through DNS, Desomnia can query that information automatically — and supplement it with addresses it learns from other sources.

The ``autoDetect`` attribute
-----------------------------

The ``autoDetect`` attribute controls which network entities Desomnia should discover dynamically. It accepts a combination of the following values, joined with the pipe operator:

.. include:: ./attributes/auto.rst

The attribute is available on both ``<NetworkMonitor>`` and on individual ``<Host>`` elements. When set on ``<NetworkMonitor>``, the value is inherited by all configured hosts as a default. Individual hosts can override it:

.. code:: xml

   <NetworkMonitor interface="eth0" autoDetect="Router|IPv4|IPv6">

     <Host name="pie" IPv4="192.168.178.5" />

     <RemoteHost name="neo" MAC="00:2B:3C:4D:5E:6F" />

     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" autoDetect="IPv4" />

   </NetworkMonitor>

Here, ``neo`` inherits the full network-level ``autoDetect="Router|IPv4|IPv6"`` and will have both its IPv4 and IPv6 addresses resolved. ``morpheus`` overrides this with ``autoDetect="IPv4"``, so only its IPv4 address is queried. ``pie`` also inherits the network default; its statically declared IPv4 address is always included alongside any dynamically discovered addresses.

Static addresses coexist with dynamically discovered ones at all times — the discovery process only adds to what is already configured, never removes it.

.. note::

    The DNS lookup uses each host's ``hostName`` attribute. If that is not set, the logical ``name`` is used instead. Make sure the name resolves correctly in your local DNS before enabling auto-configuration.

.. note::

    MAC addresses cannot be resolved through DNS. For automatic MAC discovery, Desomnia relies on ARP or NDP — which only works for hosts that are currently online — or on a router plugin. See `MAC address discovery`_ below.

Verifying DNS resolution
------------------------

Before enabling auto-configuration, verify that your local DNS resolves host names correctly.

Windows
^^^^^^^

:OS: 🪟

.. code:: powershell

   nslookup morpheus

A successful result looks like this:

::

   Server:     fritz.box
   Address:    fd82:8399:3213:0123:b2f2:a44d:feta:abcd

   Name:       morpheus.fritz.box
   Addresses:  fd82:8399:3213:0123:2c73:1234:8da2:3882
               2001:0000:A23D:71e0:2c73:8d94:8da2:3882
               fd82:8399:3213:0123:1b8e:5c5b:ea23:9f78
               2001:0000:A23D:71e0:1295:09C0:876A:130B
               192.168.128.10

Linux and macOS
^^^^^^^^^^^^^^^

:OS: 🐧 🍎

.. code:: bash

   dig A morpheus       # check IPv4
   dig AAAA morpheus    # check IPv6

A successful IPv4 result:

::

   ;; QUESTION SECTION:
   ;morpheus.          IN  A

   ;; ANSWER SECTION:
   morpheus.       9   IN  A   192.168.178.10

   ;; AUTHORITY SECTION:
   morpheus.       9   IN  NS  fritz.box.

   ;; ADDITIONAL SECTION:
   fritz.box.      9   IN  A   192.168.178.1
   fritz.box.      9   IN  AAAA    fd82:8399:3213:0123:b2f2:a44d:feta:abcd
   fritz.box.      9   IN  AAAA    2001:0000:A23D:71e0:b2f2:a44d:feta:abcd

If DNS resolution does not work, you will need to fall back to static IP configuration for the time being. Some networks use alternative name resolution mechanisms — Desomnia queries multiple sources, so it is worth attempting auto-configuration even if standard DNS is unavailable.

Understanding IPv6 addresses
-----------------------------

It is normal for a single host to have many IPv6 addresses at the same time. They serve different scopes:

- Addresses beginning with ``2`` are globally routable and can be reached from the public internet.
- Addresses beginning with ``F`` (``fc…``, ``fd…``, ``fe80…``) are of limited scope — reachable only within the local network or link.

IPv6 addresses are also temporary by design: hosts frequently rotate their global addresses for privacy, reserving new ones before the old ones expire. Desomnia tracks all known addresses for each host and reacts to whichever one the network traffic actually uses.

Keeping addresses up to date
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

Because IPv6 addresses change over time, Desomnia can periodically re-query DNS to discover new ones:

.. code:: xml

   <NetworkMonitor interface="eth0" autoDetect="IPv6" autoRefresh="1h" autoTimeout="5s">
     <!-- ... -->
   </NetworkMonitor>

``autoRefresh``
  The interval at which Desomnia repeats the discovery process. If this attribute is omitted, addresses are only resolved at startup and a restart is required to pick up changes.

``autoTimeout``
  The time limit for a single resolution query. The default is 5 seconds. Increase this if your network's name resolution is slow.

On each refresh, the full set of dynamically discovered addresses is replaced by the result of the new query. Addresses that were discovered dynamically and no longer appear in the result are removed; statically configured addresses are never affected. This ensures that stale addresses do not linger after a host has rotated them.

Because hosts typically advertise new addresses before retiring old ones, a refresh interval of one hour is generally sufficient.

Unsolicited advertisements
^^^^^^^^^^^^^^^^^^^^^^^^^^^

In addition to periodic queries, Desomnia passively monitors ARP and NDP broadcasts. If a host whose MAC address is already known advertises a new IP address on the network, that address is added immediately to the known list — without waiting for the next refresh. The address is then confirmed against the name resolution authority at the next scheduled refresh; addresses that cannot be confirmed are discarded.

MAC address discovery
---------------------

MAC addresses are required for Wake-on-LAN and cannot be obtained from DNS. Desomnia can discover them through the following means:

- **ARP / NDP**: standard address resolution from an IP address. This only succeeds if the host is currently online and reachable on the network.
- **Router plugins**: many routers maintain a complete MAC-to-IP mapping table, including offline hosts. A plugin for your router model can expose this information to Desomnia's discovery phase.

The following router integrations are currently available:

- 🚧 :doc:`FRITZ!Box </modules/network/routers/fritzbox>` (planned)

If none of these sources is available for a given host, its MAC address must be configured statically.

Discovering MAC addresses manually
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

If a host is currently online, its MAC address can usually be obtained from one of the following sources.

**The host itself**
  On Windows, run ``ipconfig /all`` and look for the ``Physical Address`` field on the relevant network adapter. On Linux or macOS, use ``ip link show`` or ``ifconfig``.

**ARP table on the machine running Desomnia**
  If the host is reachable, ping it once to populate the ARP cache, then inspect the table:

  .. code:: powershell

     # Windows
     arp -a

  .. code:: bash

     # Linux / macOS
     ip neigh     # or: arp -n

  Find the entry matching the host's IP address to read its MAC.

**Router admin interface**
  Most routers expose a DHCP lease table that lists MAC addresses for all known devices, including hosts that are currently offline. Look for a page labelled *LAN*, *Connected devices*, *DHCP*, or similar in your router's web interface.
