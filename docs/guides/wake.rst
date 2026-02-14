Automatic Wake-on-LAN
=====================

:OS: 🪐 *Platform-independent*

One of Desomnia's primary modes of operation is to wake up remote hosts via the network based on automatic usage detection. In order for this to work, the program uses a OS-native userland packet capturing library to detect when you want to access a remote service and take an apropriate action, without any additional conscious effort on your part.

As the :doc:`/modules/network/monitor` module is available on all major platforms, this feature works independently of the underlying operating system.

Where to start?
---------------

To run Desomnia you need to create a configuration in XML format. The simplest configuration which tells Desomnia to monitor a :doc:`specific network interface </modules/network/interface>`, but which doesn't do anything else, looks the following:

.. code:: xml

   <?xml version="1.0" encoding="UTF-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor interface="eth0">
       <!-- host configuration goes here -->
     </NetworkMonitor>

   </SystemMonitor>

.. note:: Be aware that you can monitor any number of network interfaces. To keep things simple, we will skip the boilerplate from now on and focus on just one network configuration. Sometimes even the ``<NetworkMonitor>...</NetworkMonitor>`` part may be skipped, if there is nothing more interesting to learn from it.

Determine the scope of the watch
--------------------------------

It is also important to know, that you can decide between two watch modes here. In the default configuration Desomnia will only inspect outgoing network traffic from the local host to other endpoints in the network. To centralise your setup and provide transparent Wake-on-LAN services across the entire broadcast domain, configure Desomnia to run in :doc:`promiscuous mode </modules/network/promiscuous>`. This will enable it to detect special multicast messages and react to connection attempts between remote hosts.

The configuration will often look the same, regardless of the scope that has been decided upon. But some filter and configuration options will only make sense, when you watch a whole broadcast domain. Keep that in mind.

Define the shape of your network
--------------------------------

For Desomnia to be able to act on any network activity, it needs to be made aware of your network infrastructure. In this section you will learn how to configure everything manually. After you understood the basics, you should consider switching to :doc:`automatic configuration </modules/network/auto>` techniques as much as possible.

Watch a host
~~~~~~~~~~~~

.. code:: xml

   <NetworkMonitor interface="eth0">
     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10" />
   </NetworkMonitor>

This most basic configuration tells Desomnia, to inspect the traffic for any connection directed at the host ``"morpheus"``, identified by it's IPv4 address. The MAC address will then be used to send a Magic Packet to wake up the target host.

The problem with this is, that it will react to **every** connection attempt on the wire. Depending on the complexity of you network, this will lead easily to unwanted wake-ups. Maybe you have chatty Smart Home devices, that need to check on your Windows Server, for whatever reason. Some of the more intelligent routers will send requests once in a while, to check if the hosts are still alive, in order to render a nice green dot on the network overview. Either way: unless you keep your network exceptionally clean or only use layer 2 switches, you will soon reach the limits of this configuration. Let's discuss next how we can fix this.

Ignore the routers
~~~~~~~~~~~~~~~~~~

The first and easiest thing to do, would be to add the router to the config soup:

.. code:: xml

   <NetworkMonitor interface="eth0">
     <Router name="fritz.box" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.178.1" />

     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10" />
   </NetworkMonitor>

Now all connection attempts originating from the router will be ignored. When there is no other device on the network, except your client and ``"morpheus"``, this can already be sufficient.

Filtering basics
----------------

Host filters
~~~~~~~~~~~~

However, if you run Desomnia in promiscuous mode, at least one other player will be in the : the always-on device – probably a Raspberry PI, the device you want to entrust with keeping watch over your network. When there is absolutely no need, that ``"pie"`` needs to wake up ``"morpheus"``, you can configure it the following:

.. code:: xml

   <NetworkMonitor interface="eth0">
     
     <HostFilterRule name="pie" IPv4="192.168.178.5" type="MustNot" /> <!-- one rule to rule them all, so to speak -->

     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <HostFilterRule name="pie" IPv4="192.168.178.5" type="MustNot" /> <!-- scope the rule just to requests directed at "morpheus" -->
     </RemoteHost>
   </NetworkMonitor>

Hopefully you see, that these rules are redundant. You can either define filter rules to be valid for all watched hosts on the network or, if you have a more specific need, you may scope them to apply only to an individual host. A ``HostFilterRule`` are configured similar to hosts, with a name and a MAC or IP address. If you need the same host at multiple places, you can define the host separately and just reference it by name:

.. code:: xml

  <NetworkMonitor interface="eth0">
    <Host name="pie" IPv4="192.168.178.5" />

    <HostFilterRule name="pie" type="MustNot" />

    <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
      <HostFilterRule name="pie" type="MustNot" />
    </RemoteHost>
  </NetworkMonitor>

Host range filters
~~~~~~~~~~~~~~~~~~

When you configure or manage whole networks, it can be helpful to specify entire subnets of hosts instead of individual hosts. So in most places where you can put a ``HostFilterRule`` you can use a ``HostRangeFilterRule`` instead:

.. code:: xml

    <HostRangeFilterRule firstIP="192.168.178.100" lastIP="192.168.178.200" type="MustNot" />

    <HostRangeFilterRule network="192.168.178.0/24" type="MustNot" />
    <HostRangeFilterRule network="2a02:908:f000:40::36c/64" type="MustNot" />

You can either specify the range by first and last IP (inclusive) or using the `CIDR <https://en.wikipedia.org/wiki/Classless_Inter-Domain_Routing>`_ notation.

As with configuring individual hosts, you may define the host range once explicitly, after which it can be referenced by name throughout the configuration:

.. code:: xml

  <NetworkMonitor interface="eth0">
    <HostRange name="upper" firstIP="192.168.178.100" lastIP="192.168.178.200" />

    <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
      <HostFilterRule name="upper" type="MustNot" />
    </RemoteHost>
  </NetworkMonitor>

To be included, or not to be included?
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

With every filter rule you can decide if it should be an inclusive (whitelist) or exclusive (blacklist) filter. The default behaviour is to only allow a request for demand to proceed, unless it is filtered by a matching ``"MustNot"`` filter rule. But as soon as you start to define the first inclusive filter with ``type="Must"`` you have reversed the situation. Then no request will be successful, unless it matches **at least one** of the inclusive rules. Exclusive rules will still be applied in this mode. This change will happen as long there is one inclusive rule in the scope of the request. If you defined the inclusive rule in the scope of a single host, other hosts will be unaffected. But as soon as you define such a rule in the scope of the network, all requests will be affected. Keep that in mind.

If your client is called ``"MacBookPro"``, your configuration could also look like this:

.. code:: xml

   <NetworkMonitor interface="eth0">

     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <HostFilterRule name="MacBookPro" IPv4="192.168.178.20" type="Must" />
     </RemoteHost>

   </NetworkMonitor>

Now requests from our ``"pie"`` would still be filtered, not because they matched with a corresponding ``"MustNot"``-filter, but rather because they **don't** match with the new ``"Must"``-filter for our laptop. At this point you could add more and more devices to your (actual) network, without worrying too much about changing the configuration of Desomnia.

.. hint::
  If you just want to **exclude** something, you can safely omit the type, because ``type="MustNot"`` is the **default** type for every filter rule, if you don't set it explicitly.

.. admonition:: Style

   If you don't mind producing irregular XML, you can slightly reduce the verbosity of your configuration file, by replacing ``type="Must"`` with ``must``. This notation is also used in HTML for "`boolean attributes <https://developer.mozilla.org/en-US/docs/Glossary/Boolean/HTML>`__", so it shouldn't look too exotic:
      
   .. code:: xml

      <HostFilterRule name="MacBookPro" IPv4="192.168.178.20" must />

Advanced filtering
------------------

Service filters
~~~~~~~~~~~~~~~

But consider you want your network to be a more open and less restricted place. Having to expect a wide range of devices to do address resolutions for your watched hosts, it will inevitably create some unwanted noise. You probably don't want to hassle with every device trying to configure it to be more silent or disable unwanted networking services, especially when you don't know what devices people will bring to your house. But what you probably **do** know is, which network *services* people will/should use.

Let's imagine ``"morpheus"`` to be a linux host with SSH access enabled and you want to access it once in a while, to do some terminal administration tasks (or with an accordingly configured SSH server basically anything). Usually you don't access a SSH server by accident. It is rather more likely that you started a SSH client of your choice and entered the address of the host to connect to. This would be a most deliberate choice. If we could detect the destination port of such a connection attempt, we could narrow down the possibilities of waking the target host dramatically, without the need to explicitly configure each client host individually.

Fortunately, Desomnia supports this use case in both normal and promiscuous modes – the latter made possible by a little :doc:`address spoofing </modules/network/spoofing>`. So if we only want ``"morpheus"`` to be woken up, when someone tries to open a connection to the SSH server on port 22, the configuration could look like this:

.. code:: xml

   <NetworkMonitor interface="eth0">

     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <ServiceFilterRule name="SSH" port="22" type="Must" />
     </RemoteHost>

   </NetworkMonitor>

UDP support
^^^^^^^^^^^

I hope I'm not going too far out on a limb here, but in most cases you will probably have to deal with TCP based services, which is why this was made the default. But if you find yourself in need to wake a host in reaction to some UDP traffic, we got you covered:

.. code:: xml

   <RemoteHost name="bind" MAC="6F:D8:85:A3:A0:A8" IPv4="192.168.178.2">
     <ServiceFilterRule name="DNS" protocol="UDP" port="53" type="Must" />
   </RemoteHost>

Ping filters
~~~~~~~~~~~~

Ping requests are a special type of ICMP packet, that operates on top of the IP layer (both IPv4 and IPv6). In order to send such a packet to a host, an address resolution has to be made, which could potentially trigger a wake-up. In order to prevent this, you can filter ping requests globally on the network scope or on the scope of the watched host if you prefer.

.. code:: xml

   <NetworkMonitor interface="eth0">

     <PingFilterRule />

     <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10" />

   </NetworkMonitor>

.. caution::
  Be careful if you configure it this way while in promiscuous mode, as the addresses of all your watched hosts will be :doc:`spoofed </modules/network/spoofing>` constantly in order to rule out ping requests. Read more about why this has to be done and what possible consequences this can have for your network. Also keep in mind that you don't need to use any ``PingFilterRule``\, if you have a ``ServiceFilterRule`` with ``type="Must"`` already in place.

Compound filters
~~~~~~~~~~~~~~~~

If you need to accommodate very specific use cases, you may require "compound filters". As always, you can choose whether to filter each unique IP service inclusively or exclusively. You can then refine this further using any number of individual host or host range filters.

.. code:: xml

   <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">

    <ServiceFilterRule name="SSH" port="22" type="MustNot">
      <HostFilterRule name="pie" IPv4="192.168.178.5" type="Must">
    </ServiceFilterRule>

    <ServiceFilterRule name="RDP" port="3389" type="Must">
      <HostFilterRule name="cake" IPv4="192.168.178.22" type="MustNot" />
    </ServiceFilterRule>
     
    <ServiceFilterRule name="HTTP" port="80" type="Must">
      <HostFilterRule name="MacBookPro" IPv4="192.168.178.20" type="Must">
    </ServiceFilterRule>

    <PingFilterRule type="MustNot">
      <HostFilterRule name="pie" IPv4="192.168.178.5" type="MustNot">
    </PingFilterRule>
    
  </RemoteHost>

With this, admittedly rather contrived example, we tell Desomnia to wake ``"morpheus"`` in response to various combinations of filter rules and their respective **types**. The type of the nested rule must always be interpreted in relation to the parent rule. To comprehend what is happening, let us go through each case individually:

``<ServiceFilterRule>`` = **Must**    ×  ``<HostFilterRule>`` = **Must**
  This means that requests should only be allowed to a specific service, and these requests **must** originate from the specified host.
``<ServiceFilterRule>`` = **Must**    ×  ``<HostFilterRule>`` = **MustNot**
  This means that requests should only be allowed to a specific service, and these requests **must not** originate from the specified host.
``<ServiceFilterRule>`` = **MustNot** ×  ``<HostFilterRule>`` = **Must**
  This means filtering all requests to a specific service, but **only** when they originate from the specified host.
``<PingFilterRule>`` = **MustNot**    ×  ``<HostFilterRule>`` = **MustNot**
  This means filtering all requests to a specific service, **unless** they originate from the specified host. Instead of a specific transport service (TCP or UDP), you can also specify the ICMP (ping) service in general.



🚧 Payload filters
~~~~~~~~~~~~~~~~~~

The previous filters were all about inspecting the network metadata, like MAC and IP addresses or port numbers. But sometimes it can be necessary to look a bit deeper into the application layer to make a meaningful decision about whether to wake a particular host or not. For some protocols you thus may apply additional payload filters, that can do deep packet inspection – provided that the traffic isn't encrypted and there will already be meaningful information in the first packet.

.. admonition:: Work in progress

  Payload filters are not ready yet and and need a lot more work to be done.

HTTP filter
^^^^^^^^^^^

The Hypertext Transfer Protocol, one of the driving force of the internet, is a good example that can be filtered in such a way. Suppose you operate a web server inside your local network. In the best case you do employ some form of transport security like HTTPS, but the chances are good, that the secure channel already terminates at a reverse proxy somewhere on the edge of your network. Using the cleartext version of the protocol inside the perimeter, allows us then to define some sophisticated filter rules.

Imagine you have the quite resource hungry GitLab running on a dedicated linux host. You definitely don't want to keep this beast running like 24/7. But wouldn't it be nice to access your private little "GitHub" when you are on the road with your laptop, just by hitting the address in your web browser? In fact you could, already with everything you have learned so far – just if it weren't for these nasty little web crawlers that somehow always find their way to your top secret private subdomain, published **nowhere** on the internet. `They <https://matrix.fandom.com/wiki/Sentinel>`__ always find you.

Now having your GitLab server always waking up, when some random bot tries to access it's front page, won't make nobody happy. Fortunately we can do something about it:

.. code:: xml

   <RemoteHost name="gitlab" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.15">
     <HTTPFilterRule type="Must">

       <RequestFilterRule method="GET" version="1.1" host="gitlab.example.de" path="users/sign_in" type="Must" />

       <RequestFilterRule type="Must">
         <Header name="Accept-Language">^de-DE</Header>
         <Cookie name="preferred_language">de</Cookie>
         <Cookie name="visitor_id" />
       </RequestFilterRule>

     </HTTPFilterRule>
   </RemoteHost>

Adding a ``RequestFilterRule`` to a ``HTTPFilterRule`` makes the parent rule a compound filter rule too, so the rules to evaluate these are the same as when you nest a ``HostFilterRule`` inside a service filter. In this example the host ``"gitlab"`` will only wake up in response to a HTTP request at the default port 80. As both request filters have ``type="Must"`` any one of these will satisfy the filter, but at least one of them has to be matched.

This configuration effectively wakes the GitLab instance if you have already set the session cookie, or if you try to navigate to the login page.

All shown attributes of the ``RequestFilterRule``, including the text content of ``Cookie`` and ``Header`` will be evaluated as a regex against the corresponding fields inside the HTTP request packet, which should allow you to support a lot of crazy use-cases. You can also combine any number of ``HostFilterRule`` alongside ``RequestFilterRule`` to make the ``HTTPFilterRule`` very specific:

.. code:: xml

   <RemoteHost name="shelob" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.16">

    <ServiceFilterRule name="SSH" port="22" type="Must">

    <HTTPFilterRule port="8080" type="Must">
      <RequestFilterRule host="dev.example.com" type="Must" />
      <HostFilterRule name="pie" IPv4="192.168.178.5" type="Must" />
    </HTTPFilterRule>

   </RemoteHost>

With that configuration only requests originating from our ``"pie"`` will be able to awake ``"shelob"``, but only if they try to reach the web server listening at port 8080 to access the site "dev.example.com". Alternatively ``"shelob"`` will also be woken, if anyone tried to establish a connection to the SSH server.

Advertising services
--------------------

So far, when defining the shape of the network, we have only distinguished between different hosts. However, as it is natural to think of a network host in terms of an array of services, this should also be reflected in your configuration:

.. code:: xml

  <RemoteHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">

    <Service name="SSH" port="22" />
    <Service name="SMB" port="445" />
    <Service name="RDP" port="3389" />

    <HTTPService name="RDP" port="80" />

  </RemoteHost>

This effectively creates a ``<ServiceFilterRule>`` for each ``<Service>`` with roughly the same parameters, but with ``type="Must"`` instead of ``"MustNot"``, what would be the default otherwise. You can also add all the compound filter rules, you have seen in the previous chapters.

Besides being less verbose, this notation will have some other implications...

.. admonition:: Work in progress

  In future versions of Desomnia you will be able to advertise services of sleeping hosts via mDNS, to have full compatibility with Apple's "Sleep Proxy" protocol. For now the only benefit is to have the shorter notation, compared with ``<ServiceFilterRule>``.


Thinking outside the box
------------------------

Until now, we have only dealt with internal network traffic. To ensure this, we declared the resident router at the top of the configuration file, which automatically discarded every request that originated from outside your network, because these usually have to be *routed* into your network. That is what a router is for, after all. It is, in fact, a rather bad idea to open Desomnia to inbound traffic without taking any additional measures. The Internet is constantly being scanned by various actors to find open ports, surely sometimes out of scientific curiosity, but most of the time for rather less laudable reasons. Having Desomnia waking up devices in your network in response to random requests from the Internet would therefore lead to havoc.

If you can come up with a reason why you need this anyway, there are some ways to reduce the attack surface to nefarious agendas.

Unlocking the gate
~~~~~~~~~~~~~~~~~~

The first step to allow foreign hosts to wake up systems on your perimeter would be to relax the wake policy configured for our router (if you used this at all). You can either decide to allow your router to be a proxy for other hosts (the ones that need routing inside your network) or allow your router to wake hosts for it's own reasons. For this example it would be sufficient to set ``allowWakeByProxy="true"``.

.. code-block:: xml
  :emphasize-lines: 3

  <NetworkMonitor interface="eth0">

    <Router hostName="fritz.box" allowWake="false" allowWakeByProxy="true" />

    <ForeignHostFilterRule>
      <HostFilterRule IPv4="192.168.1.123" />

      <HostRange network="10.1.0.0/16" />
    </ForeignHostFilterRule>
  </NetworkMonitor>

Once you have configured Desomnia to respond to connection attempts arriving via your router, it is important to filter out the noise, i.e. all sources that are not part of your local subnet. Desomnia will automatically infer this information from the network interface configuration, when you add ``<ForeignHostFilterRule>``. Like ``<PingFilterRule>``, this is a special rule, which can only be used once.

When you recall all you have learned so far about `compound filters <Compound filters>`_, you will understand that these configuration items will both be of ``type="MustNot"``, to the consequence that the child filters create an exception for the parent filter. In this example we allow a single static IPv4 address and a whole subnet to trigger a response from Desomnia, while the rest of the Internet will be filtered out. The advantage of using this style instead of declaring various host (range) filter rules with type ``type="Must"`` is that you don't have to create any special rules for users on your internal network. They will all be permitted as before.

Knock, knock — who’s there?
~~~~~~~~~~~~~~~~~~~~~~~~~~~

Now imagine you want to access a service of a sleeping host inside your internal network while you are on the go and using a VPN is not a feasable solution. You would need a way to communicate the dynamic IP address of your device to Desomnia, so that it wakes the internal host on your request. In order to accommodate for this requirement, you can use the special ``<DynamicHostRange>``, which tells Desomnia how to authenticate remote IPs dynamically:

.. code:: xml

  <NetworkMonitor interface="eth0">

    <DynamicHostRange name="trusted" knockMethod="plain" knockPort="12345" knockTimeout="10s">
        <SharedSecret encoding="UTF-8">changeme</SharedSecret>
    </DynamicHostRange>

    <ForeignHostFilterRule>
      <HostRangeFilter name="trusted" />
    </ForeignHostFilterRule>

  </NetworkMonitor>

This configuration tells Desomnia to listen for an UDP packet with the password "changeme" encoded as an UTF-8 string. When this happens, the remote IP address will be added to the host range ``"trusted"`` and then removed again after 10 seconds. During this period Desomnia will wake up any of the configured internal hosts, the moment the remote client tries to access any of it's forwarded services.

.. admonition:: Style

  As you will likely use the ``<ForeignHostFilterRule>`` and a number of ``<DynamicHostRange>`` together, you can nest the latter within the former as an exception. The following declaration is therefore identical to the previous notation:
      
  .. code:: xml

    <NetworkMonitor interface="eth0">

      <ForeignHostFilterRule>
        <DynamicHostRange name="trusted" knockMethod="plain" knockPort="12345">
          <SharedSecret encoding="UTF-8">changeme</SharedSecret>
        </DynamicHostRange>
      </ForeignHostFilterRule>

    </NetworkMonitor>


The concept was borrowed from a software called `fwknop`_, which stands for "Firewall Knock Operator", and utilizes a technique called `Single Packet Authorization`_ (SPA). The idea is to encrypt and authenticate a packet based on a shared secret between the server and the client. If the server can successfully validate the remote client, it creates a temporary firewall exception, allowing the client to establish a connection. Otherwise, the client receives no response at all.

When the idea first came up, people used to send connection attempts to a predetermined series of ports, which ultimately lead the server to accept a connection on the desired port. Honoring the history behind this technique, this feature is still called "knocking" internally, although we will use the single packet variant from now on and shouldn't look back.

It’s me.
^^^^^^^^

You can use Desomnia as a sender of those "knocks", too. The network monitoring employed by the automatic Wake-on-LAN facility allows to automate this process easily:

.. code:: xml

  <NetworkMonitor interface="eth0" knockDelay="100ms" knockRepeat="2s" knockTimeout="20s">

    <RemoteHost name="dev.example.com" onServiceDemand="knock"
      knockMethod="plain" knockPort="12345"
      knockSecret="changeme" knockEncoding="UTF-8">

      <Service name="RDP" port="3389" />
      <Service name="Sunshine" port="17238" knockTimeout="40s" />

    </RemoteHost>

  </NetworkMonitor>

The entity in question is the service that you want to access. You can apply *knock options* at every level of the hierarchy to define default values that will be inherited by all services within the relevant node. To have Desomnia to do a Single Packet Authorization for you, the service needs to have ``"knock"`` configured as it's ``onDemand`` event handler. For your convenience, you may set this for every service of a remote host at once with ``onServiceDemand``.

When Desomnia observes an outbound packet to a service configured as such, it waits up to ``knockDelay`` until it asks the configured ``knockMethod`` to do it's magic. If you also configure ``knockRepeat``, it will repeat this after waiting the specified duration, until the ``knockTimeout`` expires. If the remote server has not responded by that time, the request is considered failed and discarded.

Hidden in ``plain`` sight
^^^^^^^^^^^^^^^^^^^^^^^^^

Using ``plain`` as a ``knockMethod`` allows to achieve quick results, but is primarily intended for showcasing purposes. Since everyone on the intermediate network will be able to see your "secret" and send it at their own discretion, it is comparable to leaving a post-it note with your password on your computer screen.

It is advised that any system exposed to the network wilderness for a prolonged duration uses some form of encryption.

As both the sender and receiver of "knocks", Desomnia uses a pluggable system to install additional protocols. The main repository contains an implementation of the reverse-engineered original protocol used by `fwknop`_, which we will call "FKO" for short. You can read more about this in the  :doc:`/plugins/fko` plugin documentation.


.. _`fwknop`: https://github.com/mrash/fwknop

.. _`Single Packet Authorization`: https://www.cipherdyne.org/fwknop/docs/SPA.html
