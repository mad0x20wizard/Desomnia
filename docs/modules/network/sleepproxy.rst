Sleep Proxy
===========

:OS: 🪐 *Platform-independent*

A **Sleep Proxy** is an always-on device that maintains the network presence of hosts that have gone to sleep, and wakes them again on demand. The concept and its wire protocol originate from Apple's *Bonjour Sleep Proxy* (also known as *Wake on Demand*): before a machine suspends, it hands its services over to a proxy via multicast DNS; the proxy keeps answering service discovery queries on the sleeper's behalf, and the instant a client actually tries to use one of those services, the proxy wakes the machine back up.

Desomnia can act as **either end** of this exchange:

- As a Sleep Proxy **server** — the always-on device that accepts registrations and wakes hosts. This is the role described on this page.
- As a Sleep Proxy **client** — a machine that registers its own services before suspending. Because that is part of a host's departure handshake, it is configured through the ``handoff`` attribute and documented on the :doc:`Handoff <handoff>` page.

Because it speaks the standard protocol, a Desomnia proxy can serve non-Desomnia clients, and a Desomnia client can register with a non-Desomnia proxy. Since Apple's **mDNSResponder** is the implementation with the biggest reach, the interoperability — in both directions — is covered on its own page: :doc:`Apple Compatibility <sleepproxy/apple>`.

.. hint::

   The Sleep Proxy builds directly on :doc:`promiscuous mode <promiscuous>` and the address-claiming machinery described under :doc:`Handoff <handoff>`. If you have not yet set up a working proxy, start with the :doc:`/guides/wol-proxy` guide; the Sleep Proxy adds automatic, standards-based service registration on top of that foundation.

How it works
------------

1. A host about to suspend sends a registration to the proxy, listing its MAC address, its IP addresses, and the services it wants to be kept reachable (for example RDP on port 3389).
2. The proxy grants a **lease** for a bounded duration and defends the host's addresses on the link so that connection attempts reach it. If :doc:`mDNS advertising <mdns>` is enabled, it additionally answers service-discovery queries for those services, so that browsing devices continue to find the host as if it were awake.
3. When a client tries to reach one of the registered services — or when the lease is about to expire with the :ref:`expiry action <sleepproxy-expire>` set to wake — the proxy sends a Magic Packet and the host comes back online.
4. Once the host is awake again it announces its return, the lease ends immediately, and the proxy releases everything it was holding on the host's behalf.

Enabling the proxy
------------------

The Sleep Proxy service is offered automatically when a ``<NetworkMonitor>`` runs in :doc:`promiscuous mode <promiscuous>` and is allowed to learn hosts and/or services dynamically. In practice this means combining ``watchMode="promiscuous"`` with an ``autoDetect`` value that includes ``Host`` and/or ``Service``:

.. code:: xml
   <SystemMonitor version="2">

     <NetworkMonitor watchMode="promiscuous" autoDetect="Host|Service">
       <!-- statically declared hosts may still appear here -->
     </NetworkMonitor>

   </SystemMonitor>

The distinction between the two discovery entities is important for the proxy:

``Service``
  The proxy may attach **newly registered services to hosts that already exist** in its configuration. A registration for a host it does not know is rejected.
``Host``
  The proxy may additionally **create host definitions on the fly** for hosts it has never seen, purely from their registration. Combined with ``Service`` this yields a proxy that requires no per-host configuration at all.

See :doc:`auto-configuration <auto>` for the full list of ``autoDetect`` entities and how inheritance between ``<NetworkMonitor>`` and individual hosts works.

Advertising over mDNS
+++++++++++++++++++++

Accepting registrations and waking hosts does not by itself make a sleeping host *findable* over multicast DNS (Bonjour/DNS-SD). The proxy always defends the host's addresses at the link layer, so a client that already has the host's IP address connects straight through and triggers the wake — but reaching that point can depend on two things the proxy does not do by default:

- **Resolving the host's name to an address**, if the client connects by name and the network has no other authority answering for a sleeping host. Enable ``advertiseHosts``.
- **Keeping the host's services discoverable** to service-browsing clients. Enable ``advertiseServices``.

.. code:: xml

   <NetworkMonitor watchMode="promiscuous" autoDetect="Host|Service" advertiseHosts="true" advertiseServices="true">
     <!-- ... -->
   </NetworkMonitor>

Both are off by default and independent of each other; see the :doc:`mDNS responder <mdns>` page for when each is worth turning on.

Leases
------

Every registration is bound to a lease with a finite duration. The client requests a duration — for a Desomnia client this is its ``handoffDuration`` (see :doc:`Handoff <handoff>`) — and the proxy clamps the request into the range it is willing to grant. The granted duration is returned in the registration response.

.. include:: ./options/sleepproxy/lease.rst

Choosing between proxies
------------------------

Several Sleep Proxies may be present on one network. Each advertises a four-part **metric** that lets clients pick the most suitable one — a dedicated, mains-powered, always-on device is a better proxy than an incidentally-available laptop. Clients prefer the proxy with the *lowest* metric; a Desomnia client works through the candidates from best to worst until a registration succeeds (see ``handoffRetry`` on the :doc:`Handoff <handoff>` page).

.. include:: ./options/sleepproxy/metrics.rst

.. include:: ./options/sleepproxy/port.rst

Registering with a Sleep Proxy
------------------------------

To make *this* machine register its services with a proxy before it sleeps, configure :doc:`handoff <handoff>` with ``handoff="SleepProxy"`` rather than the options on this page — those govern the accepting side. Desomnia locates a proxy to register with in one of two ways:

- **Statically**, by declaring the proxy as a ``<SleepProxy>`` host inside ``<NetworkMonitor>``.
- **Dynamically**, by enabling ``autoDetect="SleepProxy"`` so that Desomnia discovers advertised proxies on the network and registers with the one offering the best metric.

.. code:: xml

   <NetworkMonitor autoDetect="SleepProxy" handoff="SleepProxy">
     <!-- or point at a specific proxy: -->
     <SleepProxy name="proxy" IPv4="192.168.1.2" port="1234" />
   </NetworkMonitor>

.. include:: ./options/sleepproxy/discovery.rst

.. note::

   ``eager`` and ``lazy`` are mutually exclusive. A discovered proxy is forgotten again when the local host resumes, so that a proxy which has itself gone away is not trusted indefinitely.

Advanced
--------

The pages below document the machinery behind the proxy: the wire-level details of the protocol and Desomnia's implementation of it, and the specifics of interoperating with Apple's Bonjour Sleep Proxy. None of it is required for configuring or using the proxy — read on if you are curious how it works under the hood, or if you are debugging the exchange with a third-party implementation:

.. toctree::
   :maxdepth: 1

   sleepproxy/protocol
   sleepproxy/apple

See also
--------

- :doc:`Handoff <handoff>` — the departure handshake and the client-side attributes ``handoff``, ``handoffDuration``, ``handoffMTU`` and ``handoffRetry``.
- :doc:`Multicast DNS <mdns>` — how the proxy answers host and service discovery queries for sleeping hosts.
- :doc:`/guides/wol-proxy` — setting up the always-on proxy device that this feature builds on.
