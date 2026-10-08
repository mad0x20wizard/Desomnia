Platform considerations
=======================

Windows, Linux, and macOS provide different mechanisms for applications to request that the system remain awake. Desomnia can include these requests in its activity checks.

Power Requests
--------------

:OS: 🪟 *Windows*

On Windows, any process or kernel driver can create a *power request* by calling into the Win32 power management API. A power request is a named, typed instruction that tells the system to stay in a particular state for as long as the request is held. The request is automatically released when the issuing process exits or explicitly calls the corresponding clear function.

Inspecting active requests
++++++++++++++++++++++++++

The built-in ``powercfg`` tool can enumerate all currently active power requests:

.. code:: PowerShell

    powercfg /requests

.. code::

    DISPLAY:
    None.

    SYSTEM:
    [SERVICE] \Device\HarddiskVolume3\Windows\System32\svchost.exe (BackupService)
    Scheduled backup in progress

    AWAYMODE:
    None.

    EXECUTION:
    [PROCESS] \Device\HarddiskVolume3\Program Files\VideoLAN\vlc.exe
    Video playback active

Each block names a request type and lists the holders underneath it. The caller is identified by type (``PROCESS``, ``SERVICE``, ``DRIVER``) and path; the line after is the reason string the application provided when it created the request.

Request types
+++++++++++++

Windows distinguishes between the following request types:

``SYSTEM``
  Prevents the system from entering sleep or hibernate. This is the most common type and the one Desomnia monitors by default.

``DISPLAY``
  Prevents the display from turning off due to inactivity. Does not affect system sleep on its own.

``AWAYMODE``
  Enables *away mode*: the system appears asleep (screen off, fans quiet), but continues executing in a reduced-power state. Used by media centre applications.

``EXECUTION``
  Prevents Windows Runtime background task execution from being suspended. Relevant only to UWP/WinRT applications.

.. note::

    Only ``SYSTEM`` requests actually prevent the host from suspending. A ``DISPLAY`` request alone does not block sleep. Desomnia monitors only ``SYSTEM`` requests, since those are the ones that compete with the sleep actions it controls.

Limitations
+++++++++++

Although the built-in command allows for rudimentary adaptations, there are several drawbacks:

- ``powercfg /requestsoverride`` can suppress requests from specific sources, but this does not work for all API types and the overrides are lost on every reboot.
- There is no way to allow *only* named requests and block everything else.
- Kernel-mode drivers often provide minimal name or reason strings, making it impossible to tell what is holding the system awake without additional tooling.

Inhibitor Locks
---------------

:OS: 🐧 *Linux*

On Linux, the equivalent mechanism is the `Inhibitor Locks`_, managed by ``systemd-logind``. An application acquires a lock by calling the ``Inhibit`` method on the D-Bus logind interface and receives a file descriptor in return. The lock is held for as long as that file descriptor stays open — closing it (or the process dying) releases the lock automatically. There is no separate release call.

.. attention::

    When Desomnia is configured to suspend the system on idle (``onIdle="sleep"``), it issues the suspend command via logind with inhibitor checks explicitly disabled. This means the sleep action takes effect regardless of any inhibition locks held by other applications. Use the ``<PowerRequestMonitor>`` and its filter rules to determine whether Desomnia itself should treat a lock as activity and remain awake.

Inspecting active locks
+++++++++++++++++++++++

``systemd-inhibit`` can list all currently active locks:

.. code:: bash

    systemd-inhibit --list

.. code::

    WHO          UID  PID   WHAT  MODE  WHY
    firefox     1000  3421  sleep block Watching video
    gnome-shell 1000  1105  idle  block GNOME Shell
    upsd        0     812   sleep delay Uninterruptible Power Supply active

The ``WHAT`` column names the operation being inhibited, ``MODE`` describes how strictly it is enforced, and ``WHY`` carries the human-readable reason.

Inhibited operations
++++++++++++++++++++

Unlike Windows, Linux inhibition locks can target a range of system events beyond sleep. Each lock specifies one or more of the following operations (colons separate multiple values):

.. include:: inhibition/operation.rst

Inhibition modes
++++++++++++++++

Each lock also carries a mode that determines how strictly the inhibition is enforced:

.. include:: inhibition/mode.rst

.. note::

    Desomnia monitors ``sleep`` operations in ``block`` and ``block-weak`` mode by default — the same combination that would compete with its own sleep management. This is controlled by the ``watchOperation`` and ``watchMode`` attributes in the :doc:`configuration reference <config>`.

Comparing the Two Systems
--------------------------

The most significant structural difference is scope: Windows power requests target only system sleep and display state, while Linux inhibition locks can cover the entire shutdown and event-handling lifecycle. The second key difference is enforcement granularity — Linux's ``delay`` mode has no Windows equivalent and is what allows media players and backup tools to coordinate a clean shutdown with the system.

How Desomnia Represents Both
-----------------------------

Internally, both power requests and inhibition locks are modelled through a shared interface:

Name
  The application or service that holds the lock / request.
Reason
  The human-readable explanation it provided.

This common representation is what allows the ``<PowerRequestMonitor>`` and its ``<RequestFilterRule>`` elements to work identically on both platforms. The same regex-based rules match against ``Name`` and ``Reason`` regardless of whether the underlying source is a Windows power request or a Linux inhibition lock.

.. _`Inhibitor Locks`: https://systemd.io/INHIBITOR_LOCKS/

Power assertions
----------------

:OS: 🍎 *macOS*

Applications use power assertions to keep the system or display awake while
performing work. Desomnia reads the supported assertion types and exposes the
application name and reason to the same ``RequestFilterRule`` mechanism.

To inspect assertions reported by macOS:

.. code:: bash

   pmset -g assertions

Use the output and Desomnia's activity log to identify the request before
adding a filter. Desomnia's own ``sleep`` action requests system sleep even
when applications hold assertions, so include ``PowerRequestMonitor`` if
those applications should influence the decision to sleep.

Full sleep and resume notifications are distinct from background network wakes
on macOS. A background wake may not trigger ``onResume``.
