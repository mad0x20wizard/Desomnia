With the minimal configuration in place, Desomnia reacts to any connection directed at the watched host's IP address. Depending on your operating system and running applications, background traffic — such as network discovery, address resolution, or periodic health checks — can occasionally match and trigger an unwanted wake-up.

The following sections explain how to narrow this down to the connections you actually care about.

Filter by service
~~~~~~~~~~~~~~~~~

The most reliable way to prevent unwanted wake-ups is to restrict Desomnia to specific services by port number. Adding a ``<ServiceFilterRule>`` with ``type="Must"`` makes it an inclusive filter: only connections to that specific port will trigger a wake-up.

.. code:: xml

   <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
     <ServiceFilterRule name="SSH" port="22" type="Must" />
   </RemoteHost>

This configuration wakes ``"server"`` only when a TCP connection to port 22 is detected. All other connections to that host are ignored.

To react to multiple services, add one rule per port:

.. code:: xml

   <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
     <ServiceFilterRule name="SSH"  port="22"   type="Must" />
     <ServiceFilterRule name="SMB"  port="445"  type="Must" />
     <ServiceFilterRule name="RDP"  port="3389" type="Must" />
     <ServiceFilterRule name="HTTP" port="80"   type="Must" />
   </RemoteHost>

As long as at least one inclusive rule is present, any connection to a port not listed is automatically ignored.

For UDP-based services, set ``protocol="UDP"`` explicitly:

.. code:: xml

   <ServiceFilterRule name="DNS" protocol="UDP" port="53" type="Must" />

.. hint::
   The ``name`` attribute on a ``<ServiceFilterRule>`` is optional and purely descriptive. It appears in log output and helps identify which rule triggered an event.

Excluding specific services
^^^^^^^^^^^^^^^^^^^^^^^^^^^

If only a small number of services cause unwanted noise, you may prefer the opposite approach: leave the default behaviour in place — wake on any connection — and explicitly exclude the services you want to ignore using ``type="MustNot"``:

.. code:: xml

   <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
     <ServiceFilterRule name="Monitoring" port="8080" type="MustNot" />
   </RemoteHost>

Connections to port 8080 are ignored; all other connections to this host can still trigger a wake-up.

.. note::
   ``type="MustNot"`` is the default for all filter rules when no type is set explicitly. The two approaches — whitelisting with ``type="Must"`` and blacklisting with ``type="MustNot"`` — can be mixed, but the interaction can be difficult to reason about: as soon as at least one ``type="Must"`` rule is present, the filter switches to whitelist mode and ``type="MustNot"`` rules act only as exclusions within that whitelist.

Shorter notation with ``<Service>``
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

When all your rules use ``type="Must"``, the ``<Service>`` shorthand is equivalent and less verbose:

.. code:: xml

   <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
     <Service name="SSH"  port="22" />
     <Service name="SMB"  port="445" />
     <Service name="RDP"  port="3389" />
     <Service name="HTTP" port="80" />
   </RemoteHost>

Each ``<Service>`` element is equivalent to a ``<ServiceFilterRule>`` with ``type="Must"``.

.. admonition:: Work in progress

   In a future version of Desomnia, declared services will be advertised via mDNS while the host is suspended, enabling full compatibility with Apple's *Sleep Proxy* protocol. Currently the main benefit of ``<Service>`` over ``<ServiceFilterRule>`` is the shorter notation.

Filtering ping traffic
~~~~~~~~~~~~~~~~~~~~~~

Some operating systems and network tools send ICMP echo requests (pings) to check whether a host is reachable. These operate at the IP layer, before any TCP or UDP connection is established, and can trigger an unwanted wake-up if they happen to target a watched host.

If you have already configured at least one ``type="Must"`` service filter, or used ``<Service>`` declarations, ping traffic is automatically excluded — no further configuration is needed.

Without any inclusive service filter, you can suppress pings explicitly:

.. code:: xml

   <NetworkMonitor>
     <PingFilterRule />
     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10" />
   </NetworkMonitor>

Placing the rule at the ``<NetworkMonitor>`` level applies it to all watched hosts. To limit it to a specific host, place the rule inside the corresponding ``<RemoteHost>`` element.
