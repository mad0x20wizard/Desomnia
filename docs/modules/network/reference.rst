Configuration reference
=======================

This reference has been created to allow you to easily see all the available configuration options of Desomnia at a glance, as opposed to the rather prosaic explanations on the other pages.

.. include:: /concepts/time.rst

NetworkMonitor
--------------

Desomnia provides support for monitoring any number of installed network interfaces. You can specify which configuration should be used with which interface by using the ``interface`` and/or ``network`` attribute. If none of them is declared, the configuration will be valid for all interfaces with a gateway configured. See :doc:`interface` to learn more about this.

.. TODO: should sweep made be publicly configurable?

.. code:: xml

  <SystemMonitor version="1">

    <NetworkMonitor interface="eth0" network="192.168.178.0/24"

      autoDetect="IPv4|IPv6|Router|..." 
      autoTimeout="5s"
      autoRefresh="1h"

      advertise="lazy"
      advertiseIfStopped="true"

      demandTimeout="5s"
      demandForward="true"
      demandParallel="1"

      knockMethod="plain"
      knockPort="62201"
      knockProtocol="UDP"
      knockDelay="500ms"
      knockRepeat="2s"
      knockTimeout="10s"
      knockSecret="changeme"
      knockSecretAuth="..."
      knockEncoding="UTF-8"

      pingTimeout="500ms" 
      pingFrequency="1min"
      
      wakeType="auto"
      wakePort="9"
      wakeRepeat="2s"
      wakeTimeout="10s" 

      watchMode="normal"
      watchDeviceTimeout="10s"
      watchUDPPort="9"
      watchYield="false"
      
      onConnect="..."
      onDisconect="...">

      <!-- network entities -->

      <Host ... />
      <LocalHost ... /> <!-- or use it inline -->
      <RemoteHost ... />
      <Router ... />

      <HostRange ... />
      <DynamicHostRange ... />

      <!-- global packet filters -->

      <HostFilterRule ... />
      <HostRangeFilterRule ... />
      <ForeignHostFilterRule ... />
      <ServiceFilterRules ... />
      <ServiceFilterRule ... />
      <HTTPFilterRule ... />
      <PingFilterRule ... />

    </NetworkMonitor>

    <NetworkMonitor interface="eth1">
       <!-- optionally, watch additional network interfaces -->
    </NetworkMonitor>

  </SystemMonitor>

interface
+++++++++

Describes the name of the network interface to capture traffic on.

network
+++++++

Describes the network to capture traffic on. You can specify a single IP address or describe a whole subnet in the `CIDR`_ notation. The local network device must be configured accordingly for this configuration to become active.

autoDetect
++++++++++

:inherited: 
:default: ``nothing``

Desomnia can try to automatically discover the shape of your network. The values ``MAC``, ``IPv4``, ``IPv6`` and ``Services`` are inherited by all the configured hosts, while ``Router`` and ``SleepProxy`` tell Desomnia to discover the respective network entities. You can freely combine all the different values with the pipe operator. The outcome of the discovery process will vary according to the installed plugins and the possibilities of your individual network.

The following entities are available for auto-configuration:

``MAC``
  try to discover the MAC address of the configured hosts and routers
``IPv4``
   try to discover the IPv4 address of the configured hosts and routers
``IPv6``
  try to discover the IPv6 address(es) of the configured hosts and routers
``Router``
  try to discover the network routers
🚧 ``VPN``
  try to discover the available VPN devices, connected to your router (if possible)
🚧 ``SleepProxy``
  try to discover the sleep proxies on the network, to register the local services before sleep
🚧 ``Services``
  try to discover services of the remote hosts

Alternatively you may specify either ``nothing`` or ``everything`` in order to disable auto-configuration or to try to discover as much as possible.

autoTimeout
+++++++++++

:default: ``5s``

Sets the timeout, after which a single request to a name or address resolution authority (e.g. ARP, NDP, DNS, WINS, etc.) will be cancelled and the configurable entity considered as unknown.

autoRefresh
+++++++++++

If you define this attribute, Desomnia will scan repeatedly for a changed configuration, waiting for the specified duration each time.

.. include:: ./options/demand.rst

deviceTimeout
+++++++++++++

The packet capture device can sometimes become unresponsive. If you select this option, the Network Monitor will restart if no packets have been captured for the specified duration.

.. include:: ./options/knock.rst

.. include:: ./options/ping.rst

.. include:: ./options/wake.rst

watchMode
+++++++++

:default: ``normal``

This option determines the desired mode of operation:

``normal``
  The Network Monitor will only concern itself with outgoing packets, sent by the local host. In this mode remote hosts will automcatically woken up with a Magic Packet, when a program tries to access any of their services.
  
  .. note::
    Your local host will never advertise an IP address that does not belong to it.

``promiscuous``
  The Network Monitor will also react to incoming (multicast) packets, that where sent by remote hosts. In this mode you can watch an entire broadcast domain and transparently do Wake-on-LAN, without modifying either client nor server.
  
  .. warning::
    If necessary the local host may advertise IP addresses that does not belong to it.

watchUDPPort
++++++++++++

Desomnia typically uses an `EtherType <https://en.wikipedia.org/wiki/EtherType>`__ of ``0x0842`` to send and receive Magic Packets to do Wake-on-LAN, to prevent interference with actual network services. Use this attribute, to make it aware of Magic Packets encapsulated in (probably broadcasted) UDP packets on the given port. If you do not configure this and don't monitor any other UDP services, UDP packets can be filtered entirely inside the kernel space, which can dramatically reduce the CPU load in heavy UDP scenarios.

Typical port numbers used for this are ``7`` (Echo) or ``9`` (Discard).

watchYield
++++++++++

:default: ``false``

Setting this option instructs Desomnia to notify the other nodes on the network before suspending the system. Since there is no standard way to communicate this in a computer network, various techniques will be used:

- For now Desomnia will send a special "Unmagic Packet" as an ethernet broadcast, which contains it's own MAC address as sender and target. Other instances of Desomnia (presumable running in promiscuous mode) can detect this and potentially take over the local IP addresses, to ensure that the next request will wake up the local host immediately, without waiting for the IP caches of the other network nodes to expire.

.. admonition:: Work in progress

  Later, Desomnia will be able to register the local services it is watching with a Sleep Proxy on the network. The Proxy will then be responsible for advertising their presence without interruption and for waking up the local host again if any remote host tries to access them.

onConnect
+++++++++

:⚡️ event:

An action configured for this event will be executed when a network interface identififed by this configuration has connected to the network.

onDisconnect
++++++++++++

:⚡️ event:

An action configured for this event will be executed when a network interface identififed by this configuration has discconnected from the network.

Host
----

These basic properties are configured the same for every type of host:

.. code:: xml

  <Host name="laptop" hostName="DESKTOP-HUVAVK6"
    autoDetect="IPv4|IPv6" 
    MAC="00:11:22:33:44:55" 
    IPv4="192.168.178.10" 
    IPv6="192.168.178.20">

  </Host>

name
++++

This is the logical name of the host, to be referenced by filters and written to the logs. You can name this anything you like.

hostName
++++++++

:default: the logical **name**


This is the actual hostname as it is known to the available name resolution authorities. In other words: It's the name, by that your operating system can resolve the host. If you don't set this explicitly, the logical name will be used.

autoDetect
++++++++++

Configure to auto detect MAC or IP addresses. The possible values are ``MAC``, ``IPv4`` and ``IPv6``, which you can freely combine with the pipe operator. If you do not configure this, the value of the network is used.

.. include:: ./attributes/mac.rst

.. include:: ./attributes/ipv4.rst

.. include:: ./attributes/ipv6.rst

HostRange
---------

With this type you can declare an entire range of hosts. You can further include nested host ranges and individual hosts, identified by their name or IP address.

.. code:: xml

  <HostRange name="anything"
    network="192.168.178.0/24"
    firstIP="192.168.178.10" 
    lastIP="2001:0db8:85a3:0000:0000:8a2e:0370:7334">

    <Host name="morpheus" ... />
    <HostRange name="something" ... />

   </HostRange>

name
++++

This is the logical name of the host range, to be referenced by filters. You can name this anything you like.

.. include:: ./attributes/range.rst

DynamicHostRange
----------------

A ``<DynamicHostRange>`` can be configured exactly like a normal ``<HostRange>``, but has the ability to temporarily include additonal hosts, after a successful Singe Packet Authorisation (SPA). If you don't specify the knock attributes, they will be inherited from the Network Monitor configuration.

.. code:: xml

  <DynamicHostRange name="anything" ...
    
    knockMethod="plain"
    knockProtocol="UDP"
    knockPort="62201"
    knockTimeout="10s"

    proofIP="true"
    proofTime="10s">

    <Host name="morpheus" ... />
    <HostRange name="something" ... />

    <SharedSecret label="simple" ... />
    <SharedSecret label="better" ... />

  </DynamicHostRange>

knockMethod
+++++++++++

:default: ``plain``

This option determines which method Desomnia should use for the Single Packet Authorization (SPA). The core includes the ``plain`` method to quickly configure a password, that will be transmitted in clear text to the configured ``knockPort``. Please consider to install additional plugins (e.g. :doc:`/plugins/fko`) to unlock more secure knock methods.

knockProtocol
+++++++++++++

:default: ``UDP``

This option configures the protocol with which Desomnia will listen for a SPA packet.

knockPort
+++++++++

:default: ``62201``

This option configures the port at which Desomnia will listen for a SPA packet.

knockTimeout
++++++++++++

:default: ``10s``

This option specifies how long the remote host will be included in the ``<DynamicHostRange>``, after a successfull Single Packet Authorisation.

proofIP
+++++++

:default: ``false``

This specifies whether or not the remote IP address should be included in the encrypted payload.

proofTime
+++++++++

:duration:

This specifies whether the time must be included in the encrypted payload, and the acceptable deviation to accommodate minor differences in the clock times of the local and remote hosts.

SharedSecret
++++++++++++

The shared secret for the authenticating can be specified either inline with a single value or as a complex key pair, consisting of two distinct cryptographically byte sequences:

.. code::

  <SharedSecret label="simple" encoding="UTF-8">password</SharedSecret>

  <SharedSecret label="secure" encoding="Base64" passthrough="false">
    <Key>RqBObjFUM9lguaZin1CjJEK0a4FQamAB9ivXHq0/z6w=</Key>
    <AuthKey>AsJ0GS2IMgqbCf1hc9BfKpCK5vXiXs/J2ZLri+XdHCdZsarOTPTbPnwGT1bu7Q5+yjOlnK5oNHe3zyJf7A9J1g==</AuthKey>
  </SharedSecret>

label
~~~~~

A custom label, to further reference the shared secret.

encoding
~~~~~~~~

Possible values are ``UTF-8`` and ``Base64``.

passthrough
~~~~~~~~~~~

:default: ``false``

.. admonition:: Work in progress

  Future versions of Desomnia will allow for the received SPA packet to be forwared to a host further down the connection, if the Network Monitor is configured to do a Single Packet Authorisation itself.

LocalHost
---------

Inside ``<LocalHost>`` you can declare the services and virtual machines, that should be watched for :doc:`local sleep management </guides/sleep>`:

.. code:: xml

  <NetworkMonitor interface="eth0" ... >

    <LocalHost minTraffic="1MB/s"
      demandTimeout="5s"
      demandParallel="1">

      <Service name="SSH" ... />
      <HTTPService name="HTTP" ... />

      <HostFilterRule ... />
      <HostRangeFilterRule ... />

      <VirtualHost name="gitlab" ... />

    </LocalHost>

  </NetworkMonitor>

To simplify your configuration and reduce unnecessary verbosity, you are allowed to omit the ``<LocalHost>`` tags, and include your services and virtual hosts directly under the ``<NetworkMonitor>``. The following configuration is behaviourally equivalent to the previous one:

.. code:: xml

  <NetworkMonitor interface="eth0" ...
    minTraffic="1MB/s"

    demandTimeout="5s"
    demandParallel="1">

    <Service name="SSH" ... />
    <HTTPService name="HTTP" ... />

    <HostFilterRule ... />
    <HostRangeFilterRule ... />

    <VirtualHost name="gitlab" ... />

  </NetworkMonitor>

.. attention::

  Since some of the attributes of ``<LocalHost>`` also exist on the ``<NetworkMonitor>`` there is a slight difference, if you include some ``<RemoteHost>`` in your configuration. This is because the ambiguous attributes are actually interpreted as attributes of the ``<NetworkMonitor>``, which are inherited as defaults by all hosts of the configuration. If this is undesired, you can always use the first notation.

.. include:: ./attributes/traffic.rst

.. include:: ./options/demand.rst
   :start-after: .. simple ..

*Watched hosts*
---------------

Watched hosts (``<RemoteHost>`` and ``<VirtualHost>``) extend the ``<Host>`` and both share these additional properties:

.. code:: xml

  <RemoteHost name="morpheus" ...
    minTraffic="1MB/s"

    advertise="lazy"

    demandTimeout="5s"
    demandForward="true"
    demandParallel="1"

    onMagicPacket="wake"
    onServiceDemand="knock"
    onDemand="wake"
    onIdle="suspend"
    onStart=""
    onSuspend=""
    onStop="">

    <Service name="SSH" ... />
    <HTTPService name="HTTP" ... />

    <HostFilterRule ... />
    <HostRangeFilterRule ... />
    <ServiceFilterRule ... />
    <HTTPFilterRule ... />

    <PingFilterRule ... />

    <VirtualHost name="remote" ... />

  </RemoteHost>

  <VirtualHost name="local" ... />

.. include:: ./attributes/traffic.rst

.. include:: ./options/demand.rst

onMagicPacket
+++++++++++++

:⚡️ event:

An action configured for this event will be executed when the Network Monitor detects a Magic Packet targeted at the host that originated from a different source.

onServiceDemand
+++++++++++++++

:⚡️ event:
:inherited:

An action configured for this event will be executed when the Network Monitor detects, that a configured service on this host is being accessed.

onDemand
++++++++

:⚡️ event:
:default: ``wake``

An action configured for this event will be executed when the Network Monitor detects, that the host is offline and being accessed.

onIdle
++++++

:⚡️ event:

An action configured for this event will be executed when Desomnia identifies the host as idle.

onStart
+++++++

:⚡️ event:

An action configured for this event will be executed when the Network Monitor detects that the host has started up.

onSuspend
+++++++++

:⚡️ event:

An action configured for this event will be executed when the Network Monitor detects that the host has suspended itself. This only works if the host sends an appropriate notification before going to sleep.

onStop
++++++

:⚡️ event:

An action configured for this event will be executed when the Network Monitor detects that the host has disconnected from the network without providing a reason.

RemoteHost
----------

For remote hosts you can configure these additional properties:

.. code:: xml

   <RemoteHost name="morpheus" ...
      advertiseIfStopped="true"

      knockMethod="plain"
      knockPort="62201"
      knockProtocol="UDP"
      knockDelay="500ms"
      knockRepeat="2s"
      knockTimeout="10s"
      knockSecret="changeme"
      knockSecretAuth="..."
      knockEncoding="UTF-8"

      pingTimeout="500ms" 
      pingFrequency="1min"
      
      wakeType="auto"
      wakePort="9"
      wakeRepeat="2s"
      wakeTimeout="10s">

     <VirtualHost name="gitlab" ... />

   </RemoteHost>

.. include:: ./options/knock.rst

.. include:: ./options/ping.rst

.. include:: ./options/wake.rst

VirtualHost
-----------

Virtual hosts can only be configured in the context of a ``<RemoteHost>`` or the ``<LocalHost>``. Apart from that, everything is configured like any watched host:

.. code-block:: xml
  :emphasize-lines: 7

   <RemoteHost name="morpheus" ... >
    <VirtualHost name="gitlab" ... />
   </RemoteHost>

   <LocalHost>
    <VirtualHost name="gitlab" ...
      onMagicPacket="wake" />
   </LocalHost>

onMagicPacket
+++++++++++++

:⚡️ event:
:default: ``wake``

.. note::
  Take notice, that the default action for local virtual machines will be to ``wake`` the host. This will allow the virtual host to be waken by a Magic Packet, like any physical host.

Router
------

A router shares the same attributes as any other host on the network, but has the following additional attributes:

.. code:: xml

   <Router name="fritz.box" IPv4="192.168.178.1" ...
     allowWake="false"
     allowWakeByRemote="false"
     allowWakeOnLAN="true"

     vpnTimeout="500ms"
     vpnFrequency="1min">

     <VPNClient name="WireGuard VPN" IPv4="192.168.178.201" ... />

   </Router>

allowWake
+++++++++

:default: ``false``

Specifies whether the router will be allowed to complete demand requests. The default value is ``false``, meaning all packets originating from the router fill be filtered. Setting this value to ``true`` renders all subsequent attributes unnecessary.

allowWakeByProxy
++++++++++++++++

:default: ``false``

Specifies whether or not remote systems, whose packets are forwarded by this router, will be allowed to complete demand requests. Setting this value to ``true`` renders all subsequent attributes unnecessary.

The difference to the previous setting is, that the router still won't be allowed to successfully trigger a wake on it's own, but will receive responses to address resolution requests in order to enable it to forward IP packets from outside of the local network to a sleeping watched host.

allowWakeOnLAN
++++++++++++++

:default: ``true``

Specifies if an exception should be made, regarding the previous settings, if we receive a MagicPacket from the router prior to a connection attempt.

vpnTimeout
++++++++++

:default: ``500ms``

Specifies the timeout after it should be assumed that no VPN clients are connected. If no VPN clients are configured, this attribute has no effect.

vpnFrequency
++++++++++++

Specifies the interval at which the presence of VPN clients should be checked. This is an optional feature, so there is no default value. If no VPN clients are configured, this attribute has no effect.

.. admonition:: Work in progress

  Periodic checking of VPN clients is currently not implemented, which is why this attribute has no effect. Meanwhile please use ``vpnTimeout`` instead.

VPNClient
+++++++++

VPN clients can only be configured in the context of a router. Apart from that, everything is configured like any ``<Host>``:

.. caution::
  
  Depending on the type of VPN server you have, it probably won't be possible to resolve VPN clients by name, which is why you should provide a static IP mapping, in order to successfully check the reachability of the client. For more information about :doc:`vpn`, read the corresponding wiki page.

HostFilterRule
--------------

This rule configures the embedded packet filter to either include or exclude requests that originate from the specified network host:

.. code:: xml

   <HostFilterRule name="pie" type="MustNot"
     IPv4="192.168.178.10" 
     IPv6="2001:0db8:85a3:0000:0000:8a2e:0370:7334">

   </HostFilterRule>

.. include:: /attributes/filtertype.rst

name
++++

This is the logical name of the host, to be referenced by this filter. In order to use a shared ``<Host>`` entry, all other host related attributes have to be omitted.

.. include:: ./attributes/ipv4.rst

.. include:: ./attributes/ipv6.rst

HostRangeFilterRule
-------------------

This rule configures the embedded packet filter to either include or exclude requests that originate from the specified network host range:

.. code:: xml

  <HostRangeFilterRule name="anything" type="MustNot"
    network="192.168.178.0/24"
    firstIP="192.168.178.10" 
    lastIP="192.168.178.20">

  </HostFilterRule>

.. include:: /attributes/filtertype.rst

name
++++

This is the logical name of the host range, to be referenced by this filter. In order to use a shared ``<HostRange>`` entry, all other host range related attributes have to be omitted.

.. include:: ./attributes/range.rst

ForeignHostFilterRule
---------------------

The ``<ForeignHostFilterRule>`` does not have any attributes, besides the mandatory ``type``, because it derives its configuration from the contextual network interface and matches with all hosts, that are not part of the local subnet:

.. code-block:: xml
  :emphasize-lines: 6

  <ForeignHostFilterRule type="MustNot">

    <HostFilterRule ... />
    <HostRangeFilterRule ... />

    <DynamicHostRange name="dynamic" ... /> <!-- this is a shortcut -->

  </ForeignHostFilterRule>

.. include:: /attributes/filtertype.rst

.. note::

  You are usually not allowed to put network entities directly inside a filter. However, since this is such a common use case, you can put a ``<DynamicHostRange>`` directly inside the ``<ForeignHostFilterRule>``. This is considered to be as if declared seperately and then referenced from inside the ``<ForeignHostFilterRule>`` via a ``<HostRangeFilterRule>``. In any case, you must always specify a name for the ``<DynamicHostRange>``.

  .. code:: xml

    <DynamicHostRange name="dynamic" ... />

    <ForeignHostFilterRule>
      <HostRangeFilterRule name="dynamic" ... />
    </ForeignHostFilterRule>

ServiceFilterRule
-----------------

This rule configures the embedded packet filter to either include or exclude requests that target the specified network service. You can include any number of ``<HostFilterRule>`` and ``<HostRangeFilterRule>`` in order to make the filter more specific:

.. code:: xml

   <ServiceFilterRule name="SSH" type="MustNot"
     protocol="TCP" 
     port="22">

     <HostFilterRule ... />
     <HostRangeFilterRule ... />

   </ServiceFilterRule>

.. include:: /attributes/filtertype.rst

name
++++

To document which service this filter targets, you can specify a custom name here.

protocol
++++++++

:default: ``TCP``

Filter packets to services using the specified IP protocol. Possible values are:

``TCP``
  Transmission Control Protocol
``UDP``
  User Datagram Protocol

port
++++

:required:

Filter packets to services listening at the specified port number.

🚧 HTTPFilterRule
------------------

This filter can be configured with the same attributes as ServiceFilterRule, except that the ``port`` attribute will have the default value of ``80``.

Additionally you can configure any number of ``RequestFilterRule`` to inspect the payload of the packet. All attributes of ``RequestFilterRule`` and it's children will be matched as a regular expression to the corresponding packet field.

.. admonition:: Work in progress

  This rule is a work in progress and will be released in a future version of Desomnia.

.. code:: xml

   <HTTPFilterRule name="HTTP" type="MustNot"
     protocol="TCP" 
     port="80">

     <RequestFilterRule ... />

   </HTTPFilterRule>

RequestFilterRule
-----------------

.. code:: xml

  <RequestFilterRule type="MustNot"
    method="GET"
    path="/to/document.html"
    version="1.1"
    host="www.example.com">

    <Header name="Accept-Language">de-DE</Header>
    <Cookie name="logged_in">yes</Cookie>

  </RequestFilterRule>

.. include:: /attributes/filtertype.rst

method
++++++

The HTTP method of the request. This can be set to ``GET``, ``POST``, ``DELETE``, etc.

path
++++

The requested path, starting with ``/``.

version
+++++++

The requested HTTP version, like ``HTTP/1.1`` or ``2.0``.

host
++++

The requested domain name.

PingFilterRule
--------------

This filter checks for the presence of an ICMP echo request. There are no further attributes beside the mandatory ``type``.

.. code:: xml

   <PingFilterRule type="MustNot" />

.. include:: /attributes/filtertype.rst

Service
-------

When you configure a ``<Service>`` you can configure all the attributes of a ``<ServiceFilterRule>``, except of ``type``. An equivalent ``<ServiceFilterRule>`` will automatically be created then, but which will always have ``type="Must"``.

.. code:: xml

  <Service name="SSH" ...
    serviceName="ssh"
    
    minTraffic="10kb/s"
    
    onDemand="knock"
    onIdle="?">

    <HostFilterRule ... />
    <HostRangeFilterRule ... />

  </ServiceFilterRule>

serviceName
+++++++++++

:default: the logical **name**, but all lowercase

.. admonition:: Work in progress

  Here you will be able to define a custom service name, that will be advertised with mDNS in a later version. Ideally this name should match with it's counterpart in the `Service Name and Transport Protocol Port Number Registry <https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xml>`__, managed by IANA. If you don't configure this property, the name will be derived from the basic name property, but with all letters lowercase.

.. include:: ./attributes/traffic.rst

onDemand
++++++++

:⚡️ event:

An action configured for this event will be executed when the Network Monitor detects, that the service is being accessed.

onIdle
++++++

:⚡️ event:

An action configured for this event will be executed when Desomnia identifies the service as idle.

HTTPService
-----------

When you configure a ``<HTTPService>`` you can configure all the attributes of a ``<HTTPFilterRule>``, except of ``type``. An equivalent ``<HTTPFilterRule>`` will automatically be created then, but which will always have ``type="Must"``. It also shares all additional attributes from ``<Service>``, but has the ability to declare any number of ``RequestFilterRule`` as child filters:

.. code:: xml

  <HTTPService name="HTTP" ...
    serviceName="http"

    protocol="TCP"
    port="80"
    
    minTraffic="10kb/s"
    
    onDemand="knock"
    onIdle="?">

    <HostFilterRule ... />
    <HostRangeFilterRule ... />

    <RequestFilterRule ... />

  </ServiceFilterRule>
