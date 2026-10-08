Versioning
==========

:3:
:since: 3.3.0

Desomnia's configuration format is versioned independently of the application.

The ``version`` attribute belongs on the outermost element:

.. code:: xml

   <SystemMonitor version="3" timeout="5min" onUsage="sleepless" onIdle="sleep">
     <PowerRequestMonitor />
   </SystemMonitor>

If you use :doc:`environments <environments>`, declare the version on
``<EnvironmentMonitor>`` instead. 

.. toctree::
   :maxdepth: 2

   version/history
   version/migration

Compatibility
-------------

A file using a newer format than your installation supports cannot be loaded.
An older file can be loaded if its settings are still supported or can be
migrated. If a change cannot be made automatically, Desomnia logs the problem
and stops; correct the file before restarting the service.

Migration also runs when a file is read during automatic reload. An invalid
configuration can therefore stop a running installation. See
:doc:`/guides/troubleshooting` for recovery steps.
