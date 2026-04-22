Yielding
========

When a watched host suspends, Desomnia running in :doc:`promiscuous mode <promiscuous>` needs to claim the host's IP addresses so that connection attempts are routed to it rather than disappearing silently. The challenge is detecting the suspension without polling.

ARP and NDP cache the mapping between IP addresses and MAC addresses on every device for several minutes. If a watched host suspends and another device tries to connect before that cache entry expires, no new address resolution query is issued — the cached mapping points directly to the sleeping host, and the connection fails. Desomnia never sees it.

The ``advertise`` option (see :ref:`option-advertise`) governs when Desomnia claims addresses. The default ``lazy`` mode claims them on demand, when a connection attempt triggers a new address resolution query. ``eager`` mode claims them proactively, as soon as the host is considered offline. Detecting that a host has gone offline — without continuous polling — requires the host to announce it. This is what yielding provides.

The UnMagic Packet
------------------

When a host running Desomnia as a local resource manager suspends, it broadcasts an **UnMagic Packet** on all monitored network interfaces before the network interface is taken down. An UnMagic Packet is a standard Wake-on-LAN packet in which the target MAC address is the **sending host's own MAC address** — the inverse of a regular Magic Packet, which targets a remote host. No existing device is affected by receiving such a packet; a WoL payload targeting the sender's own MAC is meaningless to anything other than Desomnia.

Desomnia running in proxy mode on another device detects this by checking whether the WoL target MAC matches the Ethernet source address of the incoming packet.

Upon receiving an UnMagic Packet, Desomnia does not immediately claim the address. The OS takes a few seconds to complete the suspend handshake and bring the network interface down; acting immediately would produce a false negative. Desomnia instead waits for ``yieldTimeout``, then performs a reachability check against all last-known IP addresses of the departing host via ARP/NDP broadcast. Only if none respond does it consider the host offline and execute the eager address claim — overwriting the cached ARP/NDP entries on other devices so that subsequent connection attempts are routed to the proxy instead.

.. note::

   UnMagic Packet sending is currently only implemented on Windows. Linux support is planned for a future release.

yieldTimeout
++++++++++++

:inherited:
:default: ``5s``

The time Desomnia waits after receiving an UnMagic Packet before checking whether the sending host is still reachable. The value must be long enough for the OS to complete the suspend handshake and bring down the network interface; on most systems a few seconds is sufficient.

For yielding to result in an address claim, the host's ``advertise`` setting must include ``Suspend`` — either via the ``eager`` shorthand or explicitly. The UnMagic Packet is the detection mechanism for the suspend path; the ``advertise`` flags determine whether that path triggers a claim.

advertiseIfStopped
++++++++++++++++++

:inherited:
:default: ``true``

By default Desomnia claims a remote host's addresses whenever it is found to be unreachable, regardless of whether it suspended or shut down entirely. If a host implements yielding and you want to rely on the UnMagic Packet for suspension detection rather than speculative claiming on shutdown, set ``advertiseIfStopped="false"`` on that host. This suppresses the stop-based claim path while leaving the yielding-triggered suspend path intact.

.. code:: xml

   <RemoteHost name="workstation" MAC="00:1A:2B:3C:4D:5E" advertise="eager" advertiseIfStopped="false">
     <Service name="RDP" port="3389" />
   </RemoteHost>

🚧 Sleep Proxy handoff
-----------------------

Yielding is designed as a foundation for a broader sleep proxy protocol. A future version of Desomnia will support the multicast DNS Sleep Proxy protocol (originally developed by Apple), which allows sleeping hosts to register their services with a proxy via mDNS before suspending. This removes the need for manually configuring watched hosts on the proxy side, and provides a standardised path for other Sleep Proxy clients to interoperate with Desomnia. This feature is not available in the current release.
