``MAC``
  Attempt to discover the MAC address of configured hosts and routers.
``IPv4``
  Attempt to discover the IPv4 address of configured hosts and routers.
``IPv6``
  Attempt to discover the IPv6 address(es) of configured hosts and routers.
``Router``
  Attempt to locate the acting network router automatically, so that you can omit a ``<Router>`` element from your configuration.
🚧 ``VPN``
  Attempt to discover VPN devices connected to your router (if possible).
🚧 ``SleepProxy``
  Attempt to discover sleep proxies on the network, in order to register local services before sleep.
🚧 ``Services``
  Attempt to discover services advertised by remote hosts.

You may also use ``nothing`` to disable all discovery, or ``everything`` to enable all available options.
