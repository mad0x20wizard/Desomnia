Available actions
=================

As the SystemMonitor is at the top of the monitor hierarchy, these actions can be configured for every event handler, including those of nested monitors many levels below.

sleep
-----

:🔥 action:

Requests system suspension immediately. If configured, ``onSuspendTimeout`` runs after its own explicit delay when suspension has not occurred; see :doc:`config`.

sleepless
---------

:🔥 action:

This creates a system-wide power request, which will prevent the built-in power management from suspending the system. The request will be valid until the next *timeout phase*, when it has to be recreated.

reboot
------

:🔥 action:

This will shut down the computer and restart it.

shutdown
--------

:🔥 action:

This will initiate a full system shutdown.

.. caution::

    This is a final state change. After this, you will need to restart your computer manually for it to respond to requests, unless it is able to wake up from the S5 state using a Magic Packet.

exec
----

:🔥 action:

:required: ``command``
:optional: ``arguments``

:OS: 🪟 *Windows-only*

This will attempt to run the specified script or program. This process will execute in the context of the system, which means it will have the highest possible privileges. An example could look like this:

.. code:: xml

    <SystemMonitor onResume="exec('C:\stuff\sleep.ps1', 'resume')">

In this example, when the system resumes from sleep, it will execute the PowerShell script ``sleep.ps1`` with ``resume`` as command line arguments.

.. hint::

    You can use OS environment variables inside the command and arguments string. In order for them to be expanded, they have to be quoted with the percent sign character (%):

    .. code:: xml

        <SystemMonitor onResume="exec('%SystemRoot%\regedit.exe', '/e %TEMP%\backup.reg')">

    This example runs the registry editor located in ``C:\Windows`` to export the whole registry into a file ``backup.reg`` in the location for temporary files.
