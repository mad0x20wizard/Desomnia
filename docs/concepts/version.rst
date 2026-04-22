Versioning
======================

The ``<SystemMonitor>`` root element carries a ``version`` attribute that identifies the format version of the configuration file:

.. code:: xml

   <SystemMonitor version="1">
     <!-- ... -->
   </SystemMonitor>

Desomnia will refuse to start if the declared version is not supported. When a new version introduces breaking changes, this page documents what changed and how to migrate an existing configuration. If possible, Desomnia will first try to auto-migrate to the new configuration format.

Version 1
---------

:Status: Current
:Since: Initial release

Version 1 is the first and only configuration format version. There are no migration steps required.

----

Future versions will be listed here as they are introduced, together with a description of what changed and any required migration steps.
