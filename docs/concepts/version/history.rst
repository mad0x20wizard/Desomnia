Format history
============================

This page records configuration format versions and their changes.
Application release notes are published on
`GitHub releases <https://github.com/mad0x20wizard/Desomnia/releases>`_.

.. _version-3:

Version 3
---------

:current:

Root-level ``NetworkInterfaceBlock`` declarations become ``NetworkInterface``
selectors with declarative administrative state and optional monitoring exclusion.
See :doc:`interface configuration </modules/network/blocking>` for migration and
the ``disabled``, ``allowToChange``, and ``monitor`` attributes. Retired blocks
inside ``NetworkMonitor`` are removed.

.. _version-2:

Version 2
---------

:since: 3.3.0

Activity events
+++++++++++++++

``onUsage`` is the action for activity found during regular inspection. It runs
on **every inspection that reports activity**, not just when a resource first
becomes active. The usual system action is ``onUsage="sleepless"``.

``onDemand`` is reserved for incoming requests to watched network hosts,
network services, and Duo instances. For example, a connection attempt can
wake a host or start a Duo instance immediately. Both usage and demand cancel
pending idle actions.

Migration renames the former inspection action ``onDemand`` to ``onUsage`` on
``SystemMonitor``, ``ProcessMonitor``, ``Process``, ``SessionMonitor``,
``DisplayMonitor``. It also renames ``onSessionDemand`` to ``onSessionUsage`` on session processes.

On network hosts, services, and Duo ``Instance`` elements, ``onDemand``
retains its incoming-request meaning and is not renamed. These resources can
also use ``onUsage`` for periodic activity actions.

Session input settings
++++++++++++++++++++++

The session input attributes have been renamed:

- ``clockTime`` becomes ``watchInput``.
- ``clockRemote`` becomes ``watchInputRemote``.
- ``clockDisconnected`` becomes ``watchInputDisconnected``.
- ``maxIdleTime`` becomes ``maxLastInputTime``.

Duo integration
+++++++++++++++

- ``<DuoStreamMonitor>`` becomes ``<DuoSessionMonitor>``.

Review the :doc:`Duo configuration reference </plugins/duo/config>` when upgrading
custom idle rules. It explains the required session monitor and the settings
for input, application activity, and streaming traffic.

Version 1
---------

This is the initial configuration format. Files with ``version="1"``, and files with no version declaration, are read as this format.
