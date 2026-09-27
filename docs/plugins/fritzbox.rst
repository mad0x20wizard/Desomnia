FRITZ!Box
=========

:OS: 🪐 *Platform-independent*

The FRITZ!Box plugin connects Desomnia to AVM FRITZ!Box routers through the box' own APIs
(TR-064), turning the router into an active source of information: host MAC addresses are read
from its device table — *including hosts that are asleep* — VPN clients are detected
automatically, and LAN port speeds can be switched by actions. The plugin ships with every
default installation: in the Windows installer under *Router Support*, in the Docker image, and
built into the :doc:`native Linux build </installation/native/constraints>`.

.. seealso::

   :doc:`/modules/network/routers/fritzbox` documents the FRITZ!Box *hardware* behaviour that is
   independent of this plugin — its VPN network model, proxy ARP, and Wake-on-LAN constraints.

Configuration
-------------

Declare a FRITZ!Box with the ``<FRITZBoxRouter>`` element instead of a plain ``<Router>``. It
*is* a router — it supports everything :doc:`\<Router\> </modules/network/router>` does
(``allowWake…`` attributes, ``vpnTimeout``, nested ``<VPNClient>`` elements) — and adds the
connection to the box' APIs on top:

.. code:: xml

   <NetworkMonitor interface="Ethernet" watchMode="promiscuous">
     <FRITZBoxRouter username="desomnia" password="…" />

     <Host name="workstation" />
   </NetworkMonitor>

No address, name, or MAC is required: the element defaults its name to ``fritz.box``, which every
FRITZ!Box answers for, and the box' own addresses are then resolved by the ordinary host
discovery. Reaching the box is part of router creation — if it does not answer at startup, an
error is logged and no router is created.

In addition to everything :doc:`\<Router\> </modules/network/router>` supports:

.. list-table::
   :header-rows: 1
   :widths: 20 20 60

   * - Attribute
     - Default
     - Description
   * - ``name``
     - ``fritz.box``
     - Identity of the box; also how ``fritz://<name>/…`` actions address it.
   * - ``username`` / ``password``
     - *(none)*
     - Login for the authenticated APIs; see `Credentials`_. Omit both for anonymous access.
   * - ``tls``
     - ``false``
     - Talk to the box over HTTPS instead of plain HTTP.

Credentials
+++++++++++

The plugin deliberately works **without a login**. Anonymously, the box already answers:

- the complete host table with **MAC addresses, including offline/sleeping hosts** — the main
  reason to use the plugin needs no credentials at all,
- host IP addresses,
- which box in a mesh owns the internet uplink, and the network's external IPv4 address.

Supplying ``username`` and ``password`` (both must be set — a FRITZ!Box login always has a
username) adds:

- **Reliable VPN client discovery.** The authenticated host list carries an explicit VPN flag per
  peer. Without credentials, VPN peers can only be *inferred* — a host with an IP but no MAC is,
  in the box' anonymous host table, the tell-tale shape of a VPN tunnel peer. This heuristic is
  applied only when the network (or box) opted into ``autoDetect="VPN"``.
- **LAN port control.** Changing a port's speed via `fritz:// actions`_ goes through the box'
  REST API, whose session is minted over an authenticated TR-064 call.

The account needs the *FRITZ!Box settings* permission; a dedicated user for Desomnia is
recommended. The password never travels in clear text — TR-064 uses HTTP digest
authentication — so the default plain-HTTP transport is fine on a local segment. Set
``tls="true"`` to switch to HTTPS (ports 49443/443) instead; expect the box' self-signed
certificate to be accepted automatically.

Discovery
---------

With a box declared (or discovered), Desomnia gains:

- **MAC and IP discovery from the box' host table.** The box remembers every device it has ever
  leased — *including hosts that are asleep right now* and therefore absent from the ARP cache,
  which is exactly when Desomnia needs a MAC to wake them. Tracked hosts are matched by IP or
  name, and only missing information is filled in; this runs alongside the built-in ARP/NDP/DNS
  discovery (see :doc:`/modules/network/auto`).
- **Automatic VPN client discovery.** The box' VPN peers auto-populate the router's
  ``<VPNClient>`` list, so presence-gated proxy waking works without declaring each client by
  hand. Explicitly configured ``<VPNClient>`` elements take precedence.
- **Mesh awareness.** In a multi-box mesh every declared or discovered FRITZ!Box is enumerated
  with its own host table (the boxes do not report identical information), and Desomnia
  recognizes which box owns the internet uplink (the mesh master) and which are fed through the
  mesh.

Zero-configuration discovery
++++++++++++++++++++++++++++

With ``autoDetect="Router"`` on the network, no ``<FRITZBoxRouter>`` element is needed at all:
every FRITZ!Box advertises itself via mDNS (DNS-SD, ``_tr064._tcp``), and Desomnia adopts each
router it finds on the segment. FRITZ!Repeaters and powerline adapters advertise the same service
but are recognized and skipped — only actual routers (``fritz.box`` domain) become routers.
Discovered boxes are reached without credentials.

Statically declared ``<FRITZBoxRouter>`` elements do not require ``autoDetect="Router"`` —
declaring one is intent enough; the flag only governs the active lookup.

Combined with a ``<ForeignHostFilterRule>`` or ``<EveryHostFilterRule>`` — which
:doc:`automatically enables proxy wake-up </modules/network/router>` on the discovered routers —
this removes manual router discovery from the proxy configuration. You still need a working
remote-access route (for example a VPN), suitable filters, and Wake-on-LAN support on the
sleeping host. See :doc:`/guides/remote-access` for the complete setup.

fritz:// actions
----------------

Any action attribute accepts a ``fritz://`` URL that addresses a configured box by
name. This complete example increases a router port's speed while Moonlight runs
and lowers it five minutes after the process exits. Replace the router
credentials, port identifier, and process pattern with those for your setup:

.. code:: xml

   <SystemMonitor version="2">
     <NetworkMonitor>
       <FRITZBoxRouter name="fritz.box" username="desomnia" password="replace-me" />
     </NetworkMonitor>
     <ProcessMonitor>
       <Process name="Moonlight"
                onStart="fritz://fritz.box/ports/eth0?maxspeed=1000"
                onStop="fritz://fritz.box/ports/eth0?maxspeed=100+5min">Moonlight</Process>
     </ProcessMonitor>
   </SystemMonitor>

The process element's text is the executable-name pattern; ``name`` is only
its label. The router name in the URL must match the configured router.
When combining query parameters inside XML, write ``&amp;``, for example
``?maxspeed=1000&amp;eee_mode=1``.

The grammar is ``fritz://<box>/<resource>/<id>?<prop>=<value>&…``; the only resource
today is ``ports`` with a required numeric ``maxspeed`` (Mbit) and an optional
``eee_mode``. Port actions require credentials_ on the addressed box. Schedule suffixes
(``+5min``, ``+2x``) compose like on any other action and are consumed before the URL
is dispatched.

``+`` is reserved as the schedule separator on delayable attributes — a literal plus
inside a URL must be percent-encoded as ``%2B``.

.. note::
   A malformed ``fritz://`` URL (wrong segment count, missing ``maxspeed``, unknown
   box or resource) fails with a descriptive error in the log. Older releases let
   such strings fall through to the regular action resolution silently.
