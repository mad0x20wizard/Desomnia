Filtering by source host
~~~~~~~~~~~~~~~~~~~~~~~~~

In client mode, all observed traffic originates from a single machine — your own. In proxy mode, Desomnia sees connection attempts from every device on the network. This makes source host filtering an important tool for reducing noise.

The most common sources of unwanted wake-ups in proxy mode are the router and the proxy device itself.

Declaring the router
^^^^^^^^^^^^^^^^^^^^

Adding a ``<Router>`` element to your configuration tells Desomnia about your gateway. Traffic originating from the router is ignored by default, which eliminates the most common source of background noise — periodic reachability checks that many routers perform.

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" />
     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10" />
   </NetworkMonitor>

Replace the MAC and IP address values with those of your own router.

Excluding specific hosts
^^^^^^^^^^^^^^^^^^^^^^^^

To exclude the proxy device itself or any other always-on host from triggering wake-ups, add a ``<HostFilterRule>``:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" />

     <HostFilterRule name="proxy" IPv4="192.168.1.2" type="MustNot" />

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10" />
   </NetworkMonitor>

A ``<HostFilterRule>`` at the ``<NetworkMonitor>`` level applies to all watched hosts. To limit a rule to a specific host, place it inside the corresponding ``<RemoteHost>`` element instead.

If the same host is referenced in multiple places, you can define it once as a ``<Host>`` element and refer to it by name:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" />

     <Host name="proxy" IPv4="192.168.1.2" />
     <HostFilterRule name="proxy" type="MustNot" />

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <HostFilterRule name="proxy" type="MustNot" />
     </RemoteHost>
   </NetworkMonitor>

Excluding address ranges
^^^^^^^^^^^^^^^^^^^^^^^^

To exclude an entire subnet — for example, a VLAN of IoT devices that should never wake your server — use a ``<HostRangeFilterRule>`` with CIDR notation:

.. code:: xml

   <HostRangeFilterRule network="192.168.2.0/24" />

You can also specify a range by first and last address:

.. code:: xml

   <HostRangeFilterRule firstIP="192.168.1.100" lastIP="192.168.1.200" />

As with ``<HostFilterRule>``, range rules can be placed at the ``<NetworkMonitor>`` level to apply to all hosts, or inside a ``<RemoteHost>`` to apply only there. A named ``<HostRange>`` element can be defined once and referenced by name in multiple places, similar to declaring a ``<Host>`` and referencing it by name by ``<HostFilterRule>``.

Allowing external connections
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

By default, Desomnia ignores traffic that has been routed through your gateway — requests originating from outside your local network. This is intentional: without additional controls, the internet would be free to trigger wake-ups via any open port on your router.

If you want to allow specific external sources — such as a known static IP address at your workplace — to trigger wake-ups, set ``allowWakeByProxy="true"`` on your router declaration and add a ``<ForeignHostFilterRule>``:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" allowWakeByProxy="true" />

     <ForeignHostFilterRule>
       <HostFilterRule IPv4="203.0.113.42" type="MustNot" />
     </ForeignHostFilterRule>

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10" />
   </NetworkMonitor>

Desomnia automatically infers your local subnet from the network interface and protects it from interference — local devices continue to function as configured.

.. note::

    Setting ``type="MustNot"`` on the nested ``<HostFilterRule>`` to *allow* a host may seem counterintuitive. The reason is that ``<ForeignHostFilterRule>`` is itself an exclusion — it blocks all traffic originating from outside the local network. A nested ``type="MustNot"`` rule creates an exception to that exclusion, effectively permitting the specified host through. See more about the logic of *compound filters* under the next heading.

If you do not know the external IP address in advance — for example, when connecting from a mobile connection or a location with a changing IP — see the :doc:`/guides/remote-access` guide for how to authenticate dynamic addresses using Single Packet Authorization.