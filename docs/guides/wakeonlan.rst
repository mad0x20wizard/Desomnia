Automatic Wake-on-LAN
=====================

Where to start?
---------------

To run ARPergefactor you need to create a configuration in XML format. The simplest configuration which tells ARPergefactor to monitor a specific network interface, but which doesn't do anything else, looks the following:

.. code:: xml

   <?xml version="1.0" encoding="UTF-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor interface="eth0">
       <!-- host configuration goes here -->
     </NetworkMonitor>

   </SystemMonitor>

Be aware that you can configure any number of additional network interfaces here. But from now on, we will skip the boilerplate and just focus on a single network configuration. Sometimes even the ``<NetworkMonitor>...</NetworkMonitor>`` part may be skipped, if there is nothing more interesting to learn from it.

Define the shape of your network
--------------------------------

Watch a host
~~~~~~~~~~~~

.. code:: xml

   <Network interface="eth0">
     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10" />
   </Network>

What you can see is the most basic configuration, but in most cases probably also a quite useless. You told ARPergefactor to look out for a host called ``"morpheus"`` with a given MAC and IPv4 address. The IP address will be used to look for ARP requests directed to this host and the MAC address will be used to craft the Magic Packet to wake up the host.

The problem with this is, that it will react to **every** ARP packet on the Ether. Maybe you have chatty Smart Home devices, that need to check on your Windows Server, for whatever reason. Some of the more intelligent routers will send ARP requests once in a while, to check if the host is still alive, to render a nice green dot on the network overview. Either way: unless you keep your network exceptionally clean or only use layer 2 switches, you will find that configuration of little use. Let's discuss how we can fix this.

Ignore the routers
~~~~~~~~~~~~~~~~~~

The first and easiest thing to do, would be add the router to the config soup:

.. code:: xml

   <Network interface="eth0">
     <Router name="fritz.box" MAC="e8:df:70:ec:2d:fb" IPv4="192.168.178.1" />

     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10" />
   </Network>

Address resolution requests will always be ignored, when they originated from a designated router. When there is no device on the network, except your client and ``"morpheus"``, this can already be sufficient.

Filtering basics
----------------

Host filters
~~~~~~~~~~~~

But if you run ARPergefactor in the scope of the network, there will at least be on more player in the game: the always-on device, probably a Raspberry PI, which you want to entrust with keeping watch over your network. When there is absolutely no need, that ``"pie"`` needs to wake up ``"morpheus"``, you can configure it the following:

.. code:: xml

   <Network interface="eth0">
     
     <HostFilterRule name="pie" IPv4="192.168.178.5" type="MustNot" /> <!-- one rule to rule them all, so to speak -->

     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <HostFilterRule name="pie" IPv4="192.168.178.5" type="MustNot" /> <!-- scope the rule just to requests directed at "morpheus" -->
     </WatchHost>
   </Network>

Hopefully you see, that these rules are redundant. You can either define filter rules to be valid for all watched hosts on the network or, if you have a specific need, you may scope them to apply only to an individual host. ``HostFilterRule``\ s are configured similar to hosts, with a name and a MAC or IP address. If you need the same host at multiple places, you can define the host separately and just reference it by name:

.. code:: xml

   <Network interface="eth0">

     <Host name="pie" IPv4="192.168.178.5" />

     <HostFilterRule name="pie" type="MustNot" />

     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <HostFilterRule name="pie" type="MustNot" />
     </WatchHost>
   </Network>

To be included, or not to be included?
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

With every filter rule you can decide if it should be an inclusive (whitelist) or exclusive (blacklist) filter. The default behaviour is to allow each WakeRequest to proceed, unless it is filtered by a matching ``"MustNot"`` filter rule. But as soon as you start to define the first inclusive filter with ``type="Must"`` you have reversed the situation. Then no WakeRequest will be processed, unless it matches **at least one** of the inclusive rules. Exclusive rules will still be applied in this mode. This change will happen as long there is one inclusive rule in the scope of the request. If you defined the inclusive rule in the scope of a single host, other hosts will be unaffected. But as soon as you define such a rule in the scope of the network, all requests will be affected. Keep that in mind.

If your client is called ``"MacBookPro"``, your configuration could also look like this:

.. code:: xml

   <Network interface="eth0">

     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <HostFilterRule name="MacBookPro" IPv4="192.168.178.20" type="Must" />
     </WatchHost>
   </Network>

Now requests from our "pie" would still be filtered, not because they matched with a corresponding ``"MustNot"``-filter, but rather because they **don't** match with the new ``"Must"``-filter for our laptop. At this point you could add more and more devices to your (actual) network, without worrying too much about changing the configuration of ARPergefactor.

⌨️ If you don't mind producing irregular XML, you can slightly reduce the verbosity of your configuration file, by replacing ``type="Must"`` with ``must``. This notation is also used in HTML for "`boolean attributes <https://developer.mozilla.org/en-US/docs/Glossary/Boolean/HTML>`__", so it shouldn't look to exotic:

.. code:: xml

   <HostFilterRule name="MacBookPro" IPv4="192.168.178.20" must />

Advanced filtering
------------------

Service filters
~~~~~~~~~~~~~~~

But consider you want your network to be a more open and less restricted place. Having to expect a wide range of devices to do address resolutions for your watched hosts, it will inevitably create some unwanted noise. You probably don't want to hassle with every device trying to configure it to be more silent or disable unwanted networking services, especially when you don't know what devices people will bring to your house. But what you probably **do** know is, which network *services* people will/should use.

Let's imagine "morpheus" to be a linux host with SSH access enabled and you want to access it once in a while, to do some terminal administration tasks (or with an accordingly configured SSH server basically anything). Usually you don't access a SSH server by accident. It is rather more likely that you started a SSH client of your choice and entered the address of the host to connect to. This would be a most deliberate choice. If we could only detect the destination port of such a connection attempt, we could wake the target host without giving it much further thought.

Luckily ARPergefactor implements a [[technique|Impersonation]], which allows us to gather which port is being requested and use this information to filter the WakeRequests to our needs. So if we only want ``"morpheus"`` to be woken up, when someone tries to open a connection to the SSH server on port 22, the configuration would look like the following:

.. code:: xml

   <Network interface="eth0">

     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">
       <ServiceFilterRule name="SSH" port="22" type="Must" />
     </WatchHost>

   </Network>

UDP support
^^^^^^^^^^^

I hope I'm not going too far out on a limb here, but in most cases you will probably have to deal with TCP based services, which is why this was made the default. But if you find yourself in need to wake some host in reaction to some UDP traffic, we got you covered:

.. code:: xml

   <WatchHost name="bind" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.2">
     <ServiceFilterRule name="DNS" protocol="UDP" port="53" type="Must" />
   </WatchHost>

Ping filters
~~~~~~~~~~~~

Ping requests are a special type of ICMP packet, that operates on top of the IP layer (both IPv4 and IPv6). In order to send such a packet to a host, an address resolution has to be made, which could potentially trigger a WakeRequest. In order to prevent this, you can filter ping requests globally on the network scope or on the scope of the watched host if you prefer.

.. code:: xml

   <Network interface="eth0">

     <PingFilterRule />

     <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10" />

   </Network>

Maybe you noticed, that we omitted the ``type="MustNot"`` in this case. This is because, it's the **default type** for every filter rule, if you don't set it explicitly.

⚠️ Be cautious though, that if you configure it this way, all your watched hosts need to be [[impersonated|Impersonation]] constantly to rule out ping requests. Read more about why this has to be done and what possible consequences this can have for your network. Also keep in mind that you don't need to use ``PingFilterRule``\ s, if you have a ``ServiceFilterRule`` with ``type="Must"`` already in place.

Compound filters
~~~~~~~~~~~~~~~~

If you have to accommodate for very special use-cases, the need may arise to use "compound filters". These consist of a unicast filter like ``ServiceFilterRule`` or ``PingFilterRule`` encapsulated inside a host based filter rule:

.. code:: xml

   <WatchHost name="morpheus" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.10">

     <ServiceFilterRule name="RDP" port="3389" type="Must" />

     <HostFilterRule name="cake" IPv4="192.168.178.22">
       <ServiceFilterRule name="RDP" port="3389" type="MustNot" />
     </HostFilterRule>

     <HostFilterRule name="pie" IPv4="192.168.178.5">
       <ServiceFilterRule name="SSH" port="22" type="Must" />
       <PingFilterRule type="Must" />
     </HostFilterRule>

   </WatchHost>

With this, admittedly rather contrived example, we tell ARPergefactor to wake ``"morpheus"`` in response to all RDP requests, except when they are coming from a host named "cake". Also the host "pie" will have the special privilege to awake the sleeper when accessing the SSH server or by merely pinging it.

In all these cases the ``HostFilterRule``'s type will be ignored and should ideally left blank.

.. _construction-payload-filters:

🚧 Payload filters
~~~~~~~~~~~~~~~~~~

The previous filters were all about inspecting the network metadata, like MAC and IP addresses or port numbers. But sometimes it can be necessary to look a bit deeper into the application layer to make a meaningful decision about whether to wake a particular host or not. For some protocols you thus may apply additional payload filters, that can do deep packet inspection – provided that the traffic isn't encrypted and there will already be meaningful information in the first packet.

🚧 **Payload filters are not ready yet and and need a lot more work to be done.**

HTTP filter
^^^^^^^^^^^

The Hypertext Transfer Protocol, one of the driving force of the internet, is a good example that can be filtered in such a way. Suppose you operate a web server inside your local network. In the best case you do employ some form of transport security like HTTPS, but the chances are good, that the secure channel already terminates at a reverse proxy somewhere on the edge of your network. Using the cleartext version of the protocol inside the perimeter, allows us then to define some sophisticated filter rules.

Imagine you have the quite resource hungry GitLab running on a dedicated linux host. You definitely don't want to keep this beast running like 24/7. But wouldn't it be nice to access your private little "GitHub" when you are on the road with your laptop, just by hitting the address in your web browser? In fact you could, already with everything you have learned so far – just if it weren't for these nasty little web crawlers that somehow always find their way to your top secret private subdomain, published **nowhere** on the internet. `They <https://matrix.fandom.com/wiki/Sentinel>`__ always find you.

Now having your GitLab server always waking up, when some random bot tries to access it's front page, won't make nobody happy. Fortunately we can do something about it:

.. code:: xml

   <WatchHost name="gitlab" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.15">
     <HTTPFilterRule>

       <RequestFilterRule method="GET" version="1.1" host="gitlab.example.de" path="users/sign_in" type="Must" />

       <RequestFilterRule type="Must">
         <Header name="Accept-Language">^de-DE</Header>
         <Cookie name="preferred_language">de</Cookie>
         <Cookie name="visitor_id" />
       </RequestFilterRule>

     </HTTPFilterRule>
   </WatchHost>

You might argue that one could achieve the same result with less filter options set. But to show you the possibilities of this filter, most of the available attributes have been set, that actually shape the form of a request to your GitLab instance, if you navigate to the login page or access any page, after already being logged in. With both routes covered, ARPergefactor can safely distinguish between valid human generated requests and those of bots randomly passing by, keeping them effectively from waking your internal devices.

Adding ``RequestFilterRule``\ s to a ``HTTPServiceFilter`` makes the parent rule a compound filter rule too, which means that it's type will be ignored and each ``Must`` and ``MustNot`` of it's children will be evaluated separately, to check wheather the WakeRequest will be allowed to proceed. All shown attributes of the ``RequestFilterRule``, including the text content of ``Cookie`` and ``Header`` will be evaluated as a regex against the corresponding fields inside the HTTP request packet, which should allow you to support a lot of crazy use-cases.

Hopefully you will be delighted to hear, that no one will even stop you from nesting a ``RequestFilterRule`` inside a ``HTTPFilterRule`` inside a ``HostFilterRule``:

.. code:: xml

   <WatchHost name="web" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.16">

     <HostFilterRule name="pie" IPv4="192.168.178.5">
       <ServiceFilterRule name="SSH" port="3389" type="Must" />

       <HTTPFilterRule port="8080">
         <RequestFilterRule host="dev.example.com" type="Must" />
       </HTTPFilterRule>
     </HostFilterRule>

   </WatchHost>

With that configuration only requests originating from our "pie" will be able to satisfy the whitelist filter, but only if they try to reach the the web server listening at port 8080 to access the site "dev.example.com". Alternatively "web" will also be woken, it "pie" tried to establish a connection the SSH server. That's as sophisticated as it will get.

Hot reloading
-------------

You can configure ARPergefactor to register changes to your config file and automatically reinitialize itself, so that you don't have to restart the service manually. In order to do so, you have to use the command line parameter: ``arpergefactor -a`` or ``arpergefactor --auto-reload``
