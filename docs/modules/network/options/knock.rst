knockMethod
+++++++++++

:inherited: 
:default: ``plain``

This option determines which method Desomnia should use for the Single Packet Authorization (SPA), if ``knock`` is configured as a ``onDemand`` action for a particular service. The core includes the ``plain`` method to quickly configure a password, that will be transmitted in clear text to the configured ``knockPort``. Please consider to install additional plugins (e.g. :doc:`/plugins/fko`) to unlock more secure knock methods.

knockPort
+++++++++

:inherited: 
:default: ``62201``

This option configures the port against which a Single Packet Authorization will be performed.

knockProtocol
+++++++++++++

:inherited: 
:default: ``UDP``

This option configures the protocol that will be used to perform a Single Packet Authorization.

knockDelay
++++++++++

:inherited: 
:default: ``500ms``

This option specifies the amount of time to wait before performing a Single Packet Authorisation if the remote host does not respond.

knockRepeat
+++++++++++

:inherited: 

This option specifies whether to repeat the Single Packet Authorisation process and how long to wait between each attempt.

knockTimeout
++++++++++++

:inherited: 
:default: ``10s``

This option specifies how long to wait before considering the Single Packet Authorisation as failed, and when to stop sending packets.

knockSecret
+++++++++++

:inherited: 

This option specifies either the secret password to be sent to the remote host, or the key to be used to encrypt the SPA packet.

knockSecretAuth
+++++++++++++++

:inherited: 

This option specifies the key to be used for authenticating the SPA packet. If this option is not set, authentication will not be performed, which could compromise the security of the SPA.

knockSecretAuthType
+++++++++++++++++++

:inherited: 
:default: *depends on the knock method*

.. include:: /modules/network/attributes/digest.rst

knockEncoding
+++++++++++++

:inherited: 
:default: ``UTF-8``

This option specifies the encoding to be used to build the ``knockSecret`` and ``knockSecretAuth`` from the configuration file. Possible formats are ``UTF-8`` and ``Base64``.