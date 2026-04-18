Remote Access
=============

:OS: 🪐 *Platform-independent*

A VPN is the standard solution for accessing your home lab from outside your network, and Desomnia works well alongside one. However, VPN tunnels introduce additional encapsulation that adds latency — sometimes noticeably so for applications like game streaming or remote desktop sessions. Single Packet Authorization (SPA) offers an alternative: a short authentication handshake that grants access without requiring a persistent tunnel.

The current role of SPA in Desomnia is specific: it protects the wake-up mechanism from being triggered by unsolicited traffic arriving from outside your network. Port scanners and bots probing your router's forwarded ports will not know the knock sequence and cannot wake your hosts.

.. note::
   SPA authenticates *access* — it does not encrypt your data traffic. If the connection itself needs to be private, combine SPA with application-level encryption such as SSH or TLS, or use a VPN in addition.

See also: :doc:`/modules/network/vpn` and :doc:`/modules/network/knocking`.

How it works
------------

With a conventional connection, authentication happens after the connection is established. SPA reverses this: before any connection attempt is allowed through, the client sends a single short packet to the server. If the server can validate it, the client's IP address is temporarily authorized. If not, the server remains completely silent — it gives no indication that any service is running, which is effective against automated scanning.

In Desomnia, when a validated knock is received, the sender's IP is added to a temporary trusted range. Any connection attempt from that IP to a watched host during this window can trigger a wake-up. Once the window expires, the IP is removed and access is revoked.

Receiver configuration
----------------------

This section covers configuring Desomnia to listen for knock packets and authorize access on the **always-on device** running in proxy mode.

.. note::
   If you already have an SPA listener running — either another instance of Desomnia, an original `fwknop <https://github.com/mrash/fwknop>`_ server, or a service managed by someone else — you can skip this section and continue with `Sender configuration`_.

The receiver requires a proxy mode installation. If you have not done so yet, follow the :doc:`wol-proxy` guide first.

Step 1: Allow traffic routed through the gateway
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

By default, Desomnia ignores connection attempts that were routed through the gateway. To enable this, set ``allowWakeByProxy="true"`` on your router declaration and add a ``<ForeignHostFilterRule>`` to tell Desomnia to handle inbound routed traffic:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" allowWakeByProxy="true" />

     <ForeignHostFilterRule>
       <!-- authorized sources will be defined here -->
     </ForeignHostFilterRule>

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <Service name="RDP" port="3389" />
       <Service name="SSH" port="22" />
     </RemoteHost>
   </NetworkMonitor>

Without any nested rules inside ``<ForeignHostFilterRule>``, no external source is authorized yet. The next step adds the SPA listener, which grants authorization dynamically.

.. note::
   You also need to configure port forwarding on your router to make the knock port reachable from the internet. The knock port is UDP by default.

Step 2: Configure the SPA listener
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Add a ``<DynamicHostRange>`` inside the ``<ForeignHostFilterRule>``. When a valid knock packet arrives, the sender's IP is added to this range and can trigger wake-ups for the configured timeout duration:

.. code:: xml

   <ForeignHostFilterRule>
     <DynamicHostRange name="trusted" knockMethod="plain" knockPort="12345" knockTimeout="30s">
       <SharedSecret encoding="UTF-8">changeme</SharedSecret>
     </DynamicHostRange>
   </ForeignHostFilterRule>

Replace ``changeme`` with your own shared secret, and ``12345`` with the port you have forwarded on your router.

The ``knockTimeout`` defines how long the sender's IP remains authorized after a successful knock. It should be long enough for the target host to wake up and accept connections — typically 20–60 seconds depending on your hardware.

.. caution::
   The ``plain`` knock method transmits the shared secret as clear text. Anyone who can intercept the UDP packet can read the secret and replay it. This protects against port scanners and automated bots — which do not know the knock sequence — but not against an active attacker with the ability to capture network traffic.

   Use ``plain`` for testing, or in scenarios where replay attacks are not a concern. For a deployment exposed to the internet for an extended period, use the FKO method instead — see :doc:`/plugins/fko`. To generate a cryptographically strong key for use with FKO, see :doc:`/modules/network/knocking`.

Sender configuration
--------------------

This section covers configuring Desomnia to automatically send a knock before connecting to a remote service, on **the machine you connect from**.

.. note::
   If you are using an external fwknop client, configure it according to its own documentation. The following covers Desomnia's built-in knock sender, which automates this process transparently.

On the client machine, add knock attributes to the ``<RemoteHost>`` and configure the ``onServiceDemand`` event to trigger the knock automatically:

.. code:: xml

   <NetworkMonitor knockDelay="200ms" knockRepeat="3s" knockTimeout="30s">

     <RemoteHost name="server"
       onServiceDemand="knock"
       knockMethod="plain" knockPort="12345"
       knockSecret="changeme" knockEncoding="UTF-8"
       IPv4="203.0.113.1">

       <Service name="RDP" port="3389" />
       <Service name="SSH" port="22" />

     </RemoteHost>

   </NetworkMonitor>

Replace ``203.0.113.1`` with the public IP address (or hostname) of your router, and ensure the knock attributes match those configured on the receiver side.

The knock timing attributes control the sending behaviour:

``knockDelay``
  How long Desomnia waits after detecting a connection attempt before sending the knock. A short delay (100–500ms) is usually sufficient.

``knockRepeat``
  If set, Desomnia will resend the knock after this interval and continue doing so until ``knockTimeout`` expires. Useful if packets are occasionally dropped.

``knockTimeout``
  The total time Desomnia will keep trying. Should be at least as long as the receiver's ``knockTimeout`` to ensure the connection attempt falls within the authorization window.

Knock attributes can be set at the ``<NetworkMonitor>`` level as defaults, at the ``<RemoteHost>`` level for a specific host, or at the ``<Service>`` level to override per service.

Complete example
----------------

The following shows a minimal receiver and sender configuration working together using the ``plain`` method.

**Receiver** (always-on device, proxy mode):

.. code:: xml

   <?xml version="1.0" encoding="UTF-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor watchMode="promiscuous">
       <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" allowWakeByProxy="true" />

       <ForeignHostFilterRule>
         <DynamicHostRange name="trusted" knockMethod="plain" knockPort="12345" knockTimeout="30s">
           <SharedSecret encoding="UTF-8">changeme</SharedSecret>
         </DynamicHostRange>
       </ForeignHostFilterRule>

       <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
         <Service name="RDP" port="3389" />
         <Service name="SSH" port="22" />
       </RemoteHost>
     </NetworkMonitor>

   </SystemMonitor>

**Sender** (client machine, normal mode):

.. code:: xml

   <?xml version="1.0" encoding="UTF-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor knockDelay="200ms" knockRepeat="3s" knockTimeout="30s">
       <RemoteHost name="server"
         onServiceDemand="knock"
         knockMethod="plain" knockPort="12345"
         knockSecret="changeme" knockEncoding="UTF-8"
         IPv4="203.0.113.1">

         <Service name="RDP" port="3389" />
         <Service name="SSH" port="22" />

       </RemoteHost>
     </NetworkMonitor>

   </SystemMonitor>

Note that the ``IPv4`` on the sender side is the **public IP address of your router**, not the internal IP of the server. The router forwards connections to the server once the host has been woken.
