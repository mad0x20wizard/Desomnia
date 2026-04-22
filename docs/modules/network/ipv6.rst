IPv6 support
============

Desomnia supports IPv6 alongside IPv4. In many networks no special configuration is needed — if your router advertises IPv6 addresses via DNS and you have :doc:`auto-configuration <auto>` enabled, Desomnia will pick them up automatically. This page covers the cases where you want to be explicit.

Notation
--------

Wherever you can specify an IPv4 address, you can also declare an IPv6 address. The same applies to network ranges in CIDR notation:

.. code:: xml

  <NetworkMonitor interface="eth0" network="2a02:8071:51e0:71e0::/59">

    <HostFilterRule name="neo" IPv6="2a02:8071:51e0:71e0:c43f:bb3f:b00c:faf5" />

    <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E"
      IPv4="192.168.178.10"
      IPv6="2a02:8071:51e0:71e0:1048:a52:b322:dc4f" />

    <HostRange name="range" network="2a02:8071:51e0:71e0::/59" />

    <HostRange name="range"
      firstIP="2a02:8071:51e0:71e0:0000:0000:0000:0000"
      lastIP="2a02:8071:51e0:71ff:ffff:ffff:ffff:ffff" />

  </NetworkMonitor>

In practice, static IPv6 addresses are rarely useful. Unlike IPv4, where hosts are typically assigned fixed addresses, IPv6 addresses are drawn from a large pool and rotated periodically to limit long-term tracking. Static configuration is supported for completeness, but auto-configuration is the expected approach for IPv6.

Behind the scenes
-----------------

Just as `ARP`_ resolves IPv4 addresses to MAC addresses, `NDP`_ does the same for IPv6. The underlying principle is the same: when a host wants to communicate with an IP address on the local network, it broadcasts a resolution request and the owner of that address replies with its MAC address.

Desomnia relies on this mechanism when advertising IP addresses on behalf of a sleeping host. A natural question is whether this is safe to do — specifically, whether a third party could exploit the same mechanism maliciously.

NDP does include security extensions (SEND), but these are rarely deployed outside of managed enterprise networks and are not present on typical consumer hardware. In practice, the exposure is limited: any application handling sensitive data is expected to use transport-layer encryption regardless of the network it runs on, so the ability to intercept traffic at the link layer does not bypass meaningful security boundaries. Desomnia's use of NDP advertisement is consistent with this model.

.. _`ARP`: https://en.wikipedia.org/wiki/Address_Resolution_Protocol
.. _`NDP`: https://en.wikipedia.org/wiki/Neighbor_Discovery_Protocol
