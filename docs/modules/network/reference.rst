Reference
=========

This reference has been created to allow you to easily see all the available configuration options of Desomnia at a glance, as opposed to the rather prosaic explanations on the other pages.

.. include:: /concepts/time.rst

NetworkMonitor
--------------

Desomnia provides support for monitoring any number of installed network interfaces. You can specify which configuration should be used with which interface by using the ``interface`` and/or ``network`` attribute. If none of them is declared, the configuration will be valid for all interfaces with a gateway configured. See :doc:`interface` to learn more about this.

.. TODO: should sweep made be publicly configurable?

.. code:: xml

  <SystemMonitor version="1">

    <NetworkMonitor interface="eth0" network="192.168.178.0/24"

      autoDetect="nothing" 
      autoLatency="1h"
      autoTimeout="5s"

      advertise="lazy"
      advertiseIfStopped="true"

      demandTimeout="5s"
      demandForward="true"
      demandParallel="1"

      knockMethod="plain"
      knockPort="62201"
      knockProtocol="UDP"
      knockDelay="500ms"
      knockRepeat="false"
      knockTimeout="10s"
      knockSecret=""
      knockSecretAuth=""
      knockEncoding="UTF-8"

      pingTimeout="500ms" 
      pingFrequency=""
      
      wakeType="auto"
      wakePort="9"
      wakeRepeat="false"
      wakeTimeout="10s" 

      watchMode="normal"
      watchDeviceTimeout="10s"
      watchUDPPort=""
      watchYield="false">

      <!-- network entities -->

      <Host ... />
      <LocalHost ... /> <!-- or use it inline -->
      <RemoteHost ... />
      <Router ... />

      <HostRange ... />
      <DnamicHostRange ... />

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

Desomnia can try to automatically discover the shape of your network. The values ``MAC``, ``IPv4``, ``IPv6`` and ``Services`` are inherited by all the configured hosts, while ``Router`` and ``SleepProxy`` tell Desomnia to discover the respective network entities. You can freely combine all the different values with the pipe operator or separate them by comma. The outcome of the discovery process will vary according to the installed plugins and the possibilities of your individual network.

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

Defines the timeout, after which a single request to a name or address resolution authority (e.g. ARP, NDP, DNS, WINS, etc.) will be cancelled and the configurable entity considered as unknown.

autoLatency
+++++++++++

Defines the time span during which expired IP addresses may still linger in the cache of the application. Effectively this sets a timer at which interval all automatically detected IP addresses are discarded and fresh ones will be queried from the available name resolution authorities. This is an optional feature, so there is no default value.

advertise
++++++++++

:inherited:
:default: ``lazy``

This option controls when your system should advertise an IP address, that is not it's own. This only makes sense when the Network Monitor is in promiscuous mode, or when there are offline/suspended virtual machines. You can mix and match all the options using the pipe operator, or separate them with a comma.

The following options are available to specify **which address procotols** should be advertised:

``IPv4``
  advertise IPv4 addresses
``IPv6``
  advertise IPv4 addresses
``Both``
  advertise IPv4 and IPv6 addresses

The following options are available to specify **when addresses** should be advertised:

``Demand``
  advertise addresses when the remote host is requested
``Suspend``
  advertise addresses after the remote host has been suspended
``Stop``
  advertise addresses after the remote host has been stopped (manually or on disconnect)
``Resume``
  advertise addresses when the local host resumes from suspend

For your convenience there are two practical short hand notations:

``lazy``    = ``Both|Demand``
  (remote) hosts are only advertised when requested
``eager``   = ``Both|Demand|Suspend|Resume``
  (remote) hosts are advertised as soon as possible
  
  This may help in situations, where hosts resume and suspend with high frequency (less than 5min).

advertiseIfStopped
++++++++++++++++++

:inherited:
:default: ``true``

This options only affects remote hosts. Because there is no standard way of determining whether a remote host has been suspended or turned off without the host itself informing us, we have to assume that it stopped per default. A suspension can only reliably be detected if the remote host is running an instance of Desomnia or a Sleep Proxy client. You can then turn this off in order to prevent addresses for offline systems from being advertised.

pingTimeout
+++++++++++

:inherited:
:default: ``500ms``

Defines the timeout, after which a single host will be considered as unreachable, after a ARP request, NDP solicitation or ICMP echo request remain without reply. Decrease this value to accelerate WakeRequests in general. Increase this timeout to reduce the possibility of unnecessarily executed WakeRequests on a lagging network.

pingFrequency
+++++++++++++

:inherited:

Lore ipsum.


poseTimeout

  Defines the timeout, after which ARPergefactor will stop to impersonate a host during a WakeRequest and treat the request as failed, if it won't receive any unfiltered unicast IP packet during that time.

poseLatency

  Defines the time span, after which ARPergefactor will eagerly start to impersonate a host at the latest, since it received the last response from it. This effectively sets a timer, at which the availability of the host will be queried with either ARP or NDP requests. If the host is found to be unresponsive, all it's known IP addresses will be claimed as our own and advertised via ARP and NDP, until we again receive any response from that host.

  If the host sends a Magic Packet including it's own MAC address, it will be treated as a hint to skip the timer and check the availability of the host immediately after waiting ``poseTimeout``, to give the host time to complete it's suspension.

wakeTimeout

  Defines the timeout, after which a WakeRequest will be considered as failed, if we haven't received any response from the target host after the Magic Packet has been sent. There will be a warning in the log file, if this happens. Also buffered packets will only be forwarded to the target host, if the WakeRequest succeeds. The default value is 10 seconds.

wakeLatency

  Defines the time span during which no WakeRequest will be started for a given host, after the last packet originating from that host has been observed. This is primarily used to reduce the amount of noise on the link and in the logfile, so that actual WakeRequests can be tracked more easily. The default value is 5 seconds.

wakeType

  The same options can be applied here as for the host.

wakePort

  Specifies the port number for Magic Packets encapsulated in UDP packets, if ``wakeType`` was configured with ``network``. The default is ``9``. Typical port numbers used for this are ``7`` (Echo) or ``9`` (Discard). If ``wakeType`` is configure with ``link``, this attribute has no meaning.

wakeForward

  This determines, if packets received for a particular watched host should be queued during an ongoing WakeRequest and forwarded to that host, if it can successfully be waken with the configured ``wakeTimeout``. The default value is ``true``, to improve responsiveness of the connection handshake, instead of relying on the client application to resend it's request until the target host is awake. This can also reduce the likeliness of connection timeouts in the client application. You can set this option to ``false``, if you experience problems with that.

watchUDPPort

  ARPergefactor typically uses an `EtherType <https://en.wikipedia.org/wiki/EtherType>`__ of ``0x0842`` to send and receive Magic Packets for WakeOnLAN requests, to prevent interference with actual network services. Use this attribute, to make it aware of Magic Packets encapsulated in (probably broadcasted) UDP packets on a given port. This is an optional feature, so there is no default. Typical port numbers used for this are ``7`` (Echo) or ``9`` (Discard).

Host
----

These basic properties are configured the same for every type of host:

.. code:: xml

   <Host name="laptop" hostName="DESKTOP-HUVAVK6"
     autoDetect="IPv4|IPv6" 
     MAC="00:11:22:33:44:55" 
     IPv4="192.168.178.10" 
     IPv6="2001:0db8:85a3:0000:0000:8a2e:0370:7334">

   </Host>

name
~~~~

This is the logical name of the host, to be referenced by filters and written to the logs. You can name this anything you like.

hostName
~~~~~~~~

This is the actual hostname as it is known to any available name resolution authority. In other words: it's the name, by that your operating system can resolve the host. If you don't set this explicitly, the logical name will be used.

.. _autodetect-1:

autoDetect
~~~~~~~~~~

Configure to auto detect IP addresses. The possible values are ``IPv4`` and ``IPv6``, which you can freely combine with the pipe operator or separate them by comma. This is an optional feature, so there is no default value.

MAC
~~~

Configure the link layer address of this host. Possible formats are:

- ``001122334455``
- ``F0-E1-D2-C3-B4-A5``
- ``00:11:22:33:44:55``

IPv4
~~~~

Configure a static IPv4 address for this host. Use the format: ``192.168.178.10``

IPv6
~~~~

Configure a static IPv6 address for this host. Use the format: ``2001:0db8:85a3:0000:0000:8a2e:0370:7334``

⚠️ You probably don't want to configure this manually, see [[Auto configuration]] and [[IPv6 Support]]

RemoteHost
----------

For watched hosts, you configure these additional properties:

.. code:: xml

   <WatchHost name="morpheus" ...
     wakeType="auto"
     wakeRoute="broadcast"
     wakePort="9"
     wakeTimeout="10s"
     wakeLatency="5s"
     wakeForward="true"
     poseTimeout="5s"
     poseLatency="1min"
     pingTimeout="500ms"
     silent="false">

     <VirtualHost name="gitlab" ... />

   </WatchHost>

.. _waketype-1:

wakeType
~~~~~~~~

Specifies, how Magic Packets should be sent. The default is ``auto``. Possible values are:

- ``auto``: Checks if the requested IP address is in the same subnet as the configured network interface. If both hosts reside in the same network (meaning they share the same range of IP addresses), ``link`` will be used. Otherwise a combination of ``network|unicast`` will be used, because it will be unlikely then, that the target host can be reached with a link layer packet.
- ``link``: Uses packets with an an `EtherType <https://en.wikipedia.org/wiki/EtherType>`__ of ``0x0842``, that operate below the threshold of user space applications, to prevent interference with actual network services. All hosts have to be on the same subnet, in order for this to work.
- ``network``: Uses UDP packets with the Magic Packet as payload, which can potentially be routed over a tunnel connection (VPN). It may be necessary to configure a static address mapping on the router or VPN server, to reach a sleeping host. Can be observed by user space applications, that listen on the specified port number.
- ``unicast``: Send the Magic Packet directly to the device to wake up, without using broadcasts. The link layer packet will be directed at the Ethernet address of the device and potentially all the known IP addresses of the host, if configured with ``network``.
- ``none``: Don't send any Magic Packet at all. This setting exists, to temporarily disable waking up for a host.

.. _wakeport-1:

wakePort
~~~~~~~~

Specifies the port number for Magic Packets encapsulated in UDP packets, if ``wakeType`` was configured with ``network``. The default is ``9``. Typical port numbers used for this are ``7`` (Echo) or ``9`` (Discard). If ``wakeType`` is configure with ``link``, this attribute has no meaning.

.. _waketimeout-1:

wakeTimeout
~~~~~~~~~~~

Defines the timeout, after which a WakeRequest will be considered as failed, if we haven't received any response from the target host after the Magic Packet has been sent. There will be a warning in the log file, if this happens. Also buffered packets will only be forwarded to the target host, if the WakeRequest succeeds. If not specified, the value of the network will be used.

.. _wakelatency-1:

wakeLatency
~~~~~~~~~~~

Defines the time span during which no WakeRequest will be started for a given host, after the last packet originating from that host has been observed. This is primarily used to reduce the amount of noise on the link and in the logfile, so that actual WakeRequests can be tracked more easily. If not specified, the value of the network will be used.

.. _wakeforward-1:

wakeForward
~~~~~~~~~~~

This determines, if packets received for a particular watched host should be queued during an ongoing WakeRequest and forwarded to that host, if it can successfully be waken with the configured ``wakeTimeout``. The default value is ``true``, to improve responsiveness of the connection handshake, instead of relying on the client application to resend it's request until the target host is awake. This can also reduce the likeliness of connection timeouts in the client application. You can set this option to ``false``, if you experience problems with that. If not specified, the value of the network will be used.

.. _posetimeout-1:

poseTimeout
~~~~~~~~~~~

Defines the timeout, after which ARPergefactor will stop to impersonate a host during a WakeRequest and treat the request as failed, if it won't receive any unfiltered unicast IP packet during that time.

.. _poselatency-1:

poseLatency
~~~~~~~~~~~

Defines the time span, after which ARPergefactor will eagerly start to impersonate the host at the latest, since it received the last response from it. This effectively sets a timer, at which the availability of the host will be queried with either ARP or NDP requests. If the host is found to be unresponsive, all it's known IP addresses will be claimed as our own and advertised via ARP and NDP, until we again receive any response from that host.

If the host sends a Magic Packet including it's own MAC address, it will be treated as a hint to skip the timer and check the availability of the host immediately after waiting ``poseTimeout``, to give the host time to complete it's suspension.

🥷 Read more about this behavior at [[Impersonation]].

.. _pingtimeout-1:

pingTimeout
~~~~~~~~~~~

Defines the timeout, after which a single host will be considered as unreachable, after a ARP request, NDP solicitation of ICMP echo request remain without reply. If not specified, the value of the network will be used. Decrease this value to accelerate WakeRequests in general. Increase this timeout to reduce the possibility of unnecessarily executed WakeRequests on a lagging network.

VirtualHost
-----------

Virtual hosts can only be configured in the context of a watched host. Apart from that, everything is configured like ``WatchHost``.

.. code:: xml

   <VirtualHost name="gitlab"
     wakeRedirect="always">

   </VirtualHost >

wakeRedirect
~~~~~~~~~~~~

With this you can specify, how Magic Packets which carry the MAC address of the *virtual* network interface should be treated. The default behavior is to start a regular WakeRequest, which involves processing all configured filters and potentially impersonating the virtual host. If for some reason you need to wake the physical host at any rate in this particular case, set this to ``always`` in order to skip all configured filters.

Router
------

.. code:: xml

   <Router name="fritz.box" IPv4="192.168.178.1"
     allowWake="false"
     allowWakeByRemote="false"
     allowWakeOnLAN="true"
     vpnTimeout="500ms"
     vpnLatency="1min">

     <VPNClient name="WireGuard VPN" IPv4="192.168.178.201" ... />

   </Router>

allowWake
~~~~~~~~~

Specifies whether the router will be allowed to complete WakeRequests. The default value is ``false``, meaning all packets originating from the router fill be filtered. Setting this value to ``true`` renders all following attributes unnecessary.

allowWakeByProxy
~~~~~~~~~~~~~~~~

Specifies whether or not remote systems, whose packets are forwarded by this router, will be allowed to complete WakeRequests. The default value is ``false``. Setting this value to ``true`` renders all following attributes unnecessary. The difference to the previous setting is, that the router still won't be allowed to successfully complete a wake on it's own, but will receive responses to address resolution requests in order to enable it to forward IP packets from outside of the local network to a sleeping watched host.

allowWakeOnLAN
~~~~~~~~~~~~~~

Specifies if an exception should be made, regarding the previous settings, if we receive a MagicPacket from the router prior to a connection attempt.

vpnTimeout
~~~~~~~~~~

Specifies the timeout after it should be assumed that no VPN clients are connected. The default value is ``500ms``. If no VPN clients are configured, this attribute has no effect.

vpnLatency
~~~~~~~~~~

Specifies the interval at which the presence of VPN clients should be checked. This is an optional feature, so there is no default value. If no VPN clients are configured, this attribute has no effect.

🚧 Periodic checking of VPN clients is currently not implemented, which is why this attribute has no effect. Meanwhile please use ``vpnTimeout`` instead.

VPNClient
---------

VPN clients can only be configured in the context of a router. Apart from that, everything is configured like ``Host``.

⚠️ Depending on the type of VPN server you have, it probably won't be possible to resolve VPN clients by name, which is why you should provide a static IP mapping, in order to successfully check the reachability of the client. For more information about [[VPN support]], read the corresponding wiki page.

HostFilterRule
--------------

.. code:: xml

   <HostFilterRule name="pie" type="MustNot"
     hostName="raspberry"
     MAC="00:11:22:33:44:55" 
     IPv4="192.168.178.10" 
     IPv6="2001:0db8:85a3:0000:0000:8a2e:0370:7334">

   </HostFilterRule>

.. _name-1:

name
~~~~

This is the logical name of the host, to be referenced by this filter. In order to use a shared Host entry, all remaining attributes have to omitted.

.. _hostname-1:

hostName
~~~~~~~~

This is the actual hostname as it is known to any available name resolution authority. In other words: it's the name, by that your operating system can resolve the host. If you don't set this explicitly, the logical name will be used.

🚧 Auto detection is currently not supported for ``HostFilterRule``\ s directly, which is why this attribute is meaningless. Until then, you can use a Host entry in your configuration and reference it by name.

.. _mac-1:

MAC
~~~

Configure the link layer address of this host filter. Possible formats are:

- ``001122334455``
- ``F0-E1-D2-C3-B4-A5``
- ``00:11:22:33:44:55``

.. _ipv4-1:

IPv4
~~~~

Configure a static IPv4 address for this host filter. Use the format: ``192.168.178.10``

.. _ipv6-1:

IPv6
~~~~

Configure a static IPv6 address for this host filter. Use the format: ``2001:0db8:85a3:0000:0000:8a2e:0370:7334``

⚠️ You probably don't want to configure this manually, see [[Auto configuration]] and [[IPv6 Support]]

.. _servicefilterrule--service:

ServiceFilterRule / Service
---------------------------

Instead of using a ``ServiceFilterRule`` with ``type="Must"``, you can add ``Service`` nodes to your host. In both ways you will end up creating a ServiceFilterRule, except that in the latter way, it will always be a "Must"-rule. In a later version ARPergefactor will start to advertise these services with multicast DNS, to conform with Apple's `Bonjour Sleep Proxy <https://en.wikipedia.org/wiki/Bonjour_Sleep_Proxy>`__ protocol.

.. code:: xml

   <ServiceFilterRule name="SSH" type="MustNot"
     protocol="TCP" 
     port="22">

   </ServiceFilterRule>

   <!-- alternatively use -->

   <Service name="SFTP" serviceName="sftp-ssh"
     protocol="TCP" 
     port="22">

   </Service>

protocol
~~~~~~~~

Filter packets to services using the specified IP protocol. The default value is ``TCP``. Possible values are:

- ``TCP``: Transmission Control Protocol
- ``UDP``: User Datagram Protocol

port
~~~~

Filter packets to services listening at the specified port number.

serviceName
~~~~~~~~~~~

Here you can optionally define a custom service name, that will be advertised with mDNS in a later version, if you configured your rule as a ``Service``. Ideally this name should match with it's counterpart in the `Service Name and Transport Protocol Port Number Registry <https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xml>`__, managed by IANA. If you don't configure this property, the name will be derived from the basic name property, but with all letters lowercase.

.. _construction-httpfilterrule:

🚧 HTTPFilterRule
-----------------

This filter can be configured with the same attributes as ServiceFilterRule, but otherwise the shown default values will be used instead. Additionally you can configure any number of ``RequestFilterRule``\ s to inspect the payload of the **first** packet. All attributes of ``RequestFilterRule`` and it's children will be matched as a regular expression to the corresponding packet field.

🚧 **This rule is a work in progress and will be released in a future version of ARPergefactor.**

.. code:: xml

   <HTTPFilterRule name="HTTP" type="MustNot"
     protocol="TCP" 
     port="80">

     <RequestFilterRule type="MustNot"
       method="GET"
       path="/to/document.html"
       version="1.1"
       host="www.example.com">

       <Header name="Accept-Language">de-DE</Header>
       <Cookie name="logged_in">yes</Cookie>

     </RequestFilterRule>

   </HTTPFilterRule>

method
~~~~~~

The HTTP method of the request. This can be set to "GET", "POST", "DELETE", etc.

path
~~~~

The requested path, starting with a "/".

.. _version-1:

version
~~~~~~~

The requested HTTP version, like "HTTP/1.1" or "2.0".

.. _host-1:

host
~~~~

The requested domain name.

PingFilterRule
--------------

This filter checks for the presence of a ICMP echo request. There are no further attributes beside the mandatory ``type``.

.. code:: xml

   <PingFilterRule type="MustNot">

   </PingFilterRule>
