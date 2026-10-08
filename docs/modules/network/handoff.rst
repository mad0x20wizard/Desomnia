Handoff
=======

When a host suspends, the rest of the network does not immediately notice. Address resolution caches (ARP for IPv4, NDP for IPv6) keep the mapping between the host's IP addresses and its MAC address for several minutes on every device that has recently talked to it. If another device tries to connect to the sleeping host before that cache entry expires, no new resolution query is issued — the cached mapping points straight at the sleeping host, the packet is delivered to a machine that is no longer listening, and the connection fails silently. A Desomnia proxy watching the segment never sees a resolution query and therefore has no chance to wake the host.

**Handoff** is the mechanism a departing host uses to actively announce its suspension, so that another node can take over responsibility for its presence on the network *before* the first connection attempt is lost. Depending on the environment, Desomnia supports two handoff mechanisms:

- The :ref:`UnMagic Packet <handoff-unmagic>` — a lightweight broadcast understood by another Desomnia instance running in :doc:`promiscuous mode <promiscuous>` on the same segment.
- :ref:`Sleep Proxy registration <handoff-sleepproxy>` — the standardised multicast DNS Sleep Proxy protocol, in which the departing host registers its services with a :doc:`Sleep Proxy <sleepproxy>` before going to sleep.

Both mechanisms achieve the same goal — a proxy that is ready to answer for the sleeping host and wake it on demand — and they can be combined.

The ``handoff`` attribute
-------------------------

Handoff is configured on the host that is going to suspend — typically the machine on which Desomnia manages local power. Set the ``handoff`` attribute on ``<NetworkMonitor>`` (inherited by all local watches) or on an individual host. It accepts a combination of the following values, joined with ``|``:

``None``
  No handoff is performed. This is the default.
``UnMagicPacket``
  Broadcast an :ref:`UnMagic Packet <handoff-unmagic>` before suspending, for peer Desomnia proxies to pick up.
``SleepProxy``
  Register the host's watched services with a :doc:`Sleep Proxy <sleepproxy>` before suspending.
``Mandatory``
  Treat a successful handoff as a precondition for a Desomnia-initiated suspend. If every configured mechanism fails, Desomnia does not put the host to sleep. Without this flag a failed handoff is logged as a warning and the host suspends anyway.
``IPv4`` / ``IPv6``
  Restrict handoff to a single address family. If neither is given, both are handed off.

.. code:: xml

   <NetworkMonitor handoff="SleepProxy|UnMagicPacket">
     <!-- ... -->
   </NetworkMonitor>

.. note::

   ``Mandatory`` only governs suspends that Desomnia itself triggers — it holds the host awake while a required handoff is still failing. It cannot prevent the operating system, another application, or a user (for example by closing a laptop lid) from suspending the machine by other means.

handoffTimeout
++++++++++++++

:inherited:
:default: ``5s``

The time Desomnia allows for a handoff to be confirmed. On the departing host this bounds how long the suspend transition may be delayed while waiting for acknowledgement. On the receiving proxy it is the time to wait after a handoff is announced before probing whether the host has really gone — the operating system needs a few seconds to complete the suspend handshake and bring the network interface down, and acting immediately would produce a false negative.

handoffRetry
++++++++++++

:inherited:
:default: ``2``

How many additional attempts are made before a handoff is considered failed. It applies to both ends of the exchange: on the departing host, how many times a registration with a Sleep Proxy is retried — transient failures are retried against each candidate before the next-best one is tried, and the handoff only fails once every discovered proxy has been exhausted; on the receiving proxy, how many times reachability is re-checked before accepting that the host has really gone. After each ``handoffTimeout`` interval the step is repeated up to this many times. Increase it on networks where suspend takes longer to settle.

In builds after 3.3.0 that include declined-registration handling, an explicit
refusal or unsupported registration skips retries for that proxy and moves to
the next candidate. In 3.3.0, those responses also consume the retry allowance.

handoffDuration
+++++++++++++++

:inherited:
:default: ``1d``

The lease duration requested when registering with a :doc:`Sleep Proxy <sleepproxy>`. The proxy clamps the request into the range it is willing to grant — a Desomnia proxy according to its ``sleepProxyLeaseDurationMin``/``sleepProxyLeaseDurationMax``, an Apple proxy to at most 24 hours. Together with the proxy's :ref:`expiry behaviour <sleepproxy-expire>` this bounds how long the host can stay asleep before it is woken to renew its network presence.

handoffMTU
++++++++++

:inherited:
:default: *(unset)*

The largest wire size, in bytes, a single registration message may have. A registration exceeding it — for example one carrying many services — is split into a burst of smaller messages that the proxy reassembles into one registration; Apple's own client splits at ``1440``, which is a good value here as well. When unset, the registration is always sent as a single datagram and, if it exceeds the network's packet size, left to IP fragmentation. That is perfectly viable on a switched local network — a Desomnia proxy accepts fragmented registrations up to the UDP maximum of 64 KiB, Apple proxies up to roughly 9 KB — but set an MTU if fragmented UDP is filtered somewhere on the path.

handoffPassword
+++++++++++++++

:inherited:

The optional *SecureOn* Wake-on-LAN password the proxy must present in order to wake the host again. The departing host transmits it as part of a Sleep Proxy registration, so that a proxy without the password cannot wake it. As defined by the protocol, a SecureOn password is at most 6 bytes long (4- and 6-byte forms are supported); longer values are rejected. The value may be given in any supported text encoding — set ``handoffPasswordEncoding="base64"`` to supply the raw password bytes as Base64.

.. _handoff-unmagic:

The UnMagic Packet
------------------

When a host running Desomnia as a local resource manager suspends with ``handoff="UnMagicPacket"``, it broadcasts an **UnMagic Packet** on all monitored network interfaces before the interface is taken down. An UnMagic Packet is a standard Wake-on-LAN packet in which the target MAC address is the **sending host's own MAC address** — the inverse of a regular Magic Packet, which targets a remote host. No existing device is affected by receiving such a packet; a Wake-on-LAN payload targeting the sender's own MAC is meaningless to anything other than Desomnia.

Desomnia running in proxy mode on another device detects this by checking whether the Wake-on-LAN target MAC matches the Ethernet source address of the incoming packet.

Upon receiving an UnMagic Packet, the proxy does not immediately claim the address. It waits for ``handoffTimeout``, then performs a reachability check against all last-known IP addresses of the departing host via ARP/NDP broadcast (repeating up to ``handoffRetry`` times). Only if none respond does it consider the host offline and execute the eager address claim — overwriting the cached ARP/NDP entries on other devices so that subsequent connection attempts are routed to the proxy instead.

.. _handoff-sleepproxy:

Sleep Proxy handoff
-------------------

The UnMagic Packet is a Desomnia-to-Desomnia signal. For interoperability with the wider ecosystem, Desomnia also implements the multicast DNS **Sleep Proxy** protocol originally developed by Apple. Instead of merely announcing "I am leaving", a departing host with ``handoff="SleepProxy"`` **registers its services** with a Sleep Proxy on the network before suspending. The proxy then advertises those services via mDNS on the host's behalf and wakes the host again the moment a remote client tries to reach one of them.

This has two advantages over the UnMagic Packet:

- The proxy does not need the sleeping host's services configured statically — it learns them from the registration. This removes the need to maintain a matching host definition on the proxy side.
- Any standards-compliant Sleep Proxy client — not only Desomnia — can register with a Desomnia proxy, and Desomnia can register with a non-Desomnia proxy, including Apple's (an Apple TV, HomePod or AirPort base station). The compatibility details and known caveats of registering with an Apple proxy — most importantly that :doc:`wake filter rules </guides/filtering/service>` cannot be enforced there — are described on the :doc:`Apple Compatibility <sleepproxy/apple>` page.

The registration carries the host's addresses (including the reverse mappings a proxy needs to answer ARP/NDP on its behalf), its watched services with their instance names, SRV priorities/weights and TXT attributes, the requested lease (``handoffDuration``), and the optional *SecureOn* password. Individual services can be excluded from the handoff with ``handoff="false"`` on the ``<Service>`` element. When several proxies are available, Desomnia works through them from best to worst advertised metric (see ``sleepProxyMetrics``), exhausting ``handoffRetry`` against each before moving on.

When the host resumes, it announces its return on the link — the standardised signal that makes any compliant proxy, Desomnia or Apple, release the registration immediately and stop advertising on the host's behalf. No stale records linger, and the next suspend simply registers afresh.

The registering side is configured here with ``handoff="SleepProxy"``; the accepting side is covered in full on the :doc:`Sleep Proxy <sleepproxy>` page, including lease durations, waking on demand, and the dynamic creation of host and service definitions from incoming registrations. The two pages describe the two ends of the same exchange.

Address claiming on the proxy
-----------------------------

Address claiming is not tied to the UnMagic Packet — it applies to **any** handoff strategy. Whichever mechanism signals that a host has departed (an UnMagic Packet, or a Sleep Proxy registration followed by the host going quiet), the proxy uses the same logic to decide whether and when to take over the host's addresses.

That decision is governed by the ``advertise`` option (see :ref:`option-advertise`). The default ``lazy`` mode claims addresses on demand, when a connection attempt triggers a new address resolution query. ``eager`` mode claims them proactively, as soon as the host is considered offline — which is what makes handoff useful, since it provides the offline signal without continuous polling.

advertiseIfStopped
++++++++++++++++++

:inherited:
:default: ``true``

By default Desomnia claims a remote host's addresses whenever it is found to be unreachable, regardless of whether it suspended or shut down entirely. If a host implements handoff and you want to rely on the UnMagic Packet (or Sleep Proxy registration) for suspension detection rather than speculative claiming on shutdown, set ``advertiseIfStopped="false"`` on that host. This suppresses the stop-based claim path while leaving the handoff-triggered suspend path intact.

.. code:: xml

   <RemoteHost name="workstation" MAC="00:1A:2B:3C:4D:5E" advertise="eager" advertiseIfStopped="false">
     <Service name="RDP" port="3389" />
   </RemoteHost>
