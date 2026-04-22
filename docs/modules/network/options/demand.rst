advertise
+++++++++

:inherited:
:default: ``lazy``

This option controls when your system should advertise an IP address, that is not it's own. This only makes sense when the Network Monitor is in promiscuous mode, or when there are offline/suspended virtual machines. You can mix and match all the options using the pipe operator.

The following options are available to specify **which address procotols** should be advertised:

``IPv4``
  advertise IPv4 addresses
``IPv6``
  advertise IPv6 addresses
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
  
  This may be useful in situations where hosts are frequently resuming and suspending (less than 5min).

advertiseIfStopped
++++++++++++++++++

:inherited:
:default: ``true``

This options only affects remote hosts. Because there is no standard way of determining whether a remote host has been suspended or turned off without the host itself informing us, we have to assume that it stopped per default. A suspension can only reliably be detected if the remote host is running an instance of Desomnia or a Sleep Proxy client. You can then turn this off in order to prevent addresses for offline systems from being advertised.

demandForward
+++++++++++++

:inherited: 
:default: ``true``

This controls, if the original request should be forwarded to the target host, after a successful wake-up. A TCP client usually repeats it's SYN a couple of times before giving up, waiting each time a little longer. Resending the SYN immediately after the target host signals that it is ready to receive can reduce the time it takes to establish the connection. If the target host takes more than a few seconds, it can prevent a timeout on the client side in some cases. It should be safe to leave this enabled by default.

.. simple ..

demandTimeout
+++++++++++++

:inherited: 
:default: ``5s``

Sets the timeout, for how long Desomnia waits to confirm a particular demand request. This is mainly relevant if you use the Network Monitor in promiscuous mode and more than one packet is required to establish whether the host in question should be woken up. The request holds some resources which will not be released until either the request succeeds or the timeout period elapses. If you do not configure Desomnia to process more than one request at a time, this will also prevent further requests from different clients. This may lead to a reduced responsiveness, if the timeout is set too high.


demandParallel
++++++++++++++

:inherited: 
:default: ``1``

This value determines how many demand requests can be processed in parallel for any given host. For safety reasons, the default setting allows no more than one request to run in parallel, which should be sufficient for most setups. To accommodate more complex setups involving local virtual machines and a remote host running Desomnia in promiscuous mode, it may be necessary to increase this to at least ``2``.