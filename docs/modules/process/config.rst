Configuration
=============

ProcessMonitor
--------------

You can configure any number of ``<Process>`` to watch OS processes or groups of processes.

.. code:: xml

  <SystemMonitor>

    <ProcessMonitor>

      <Process ... />
      <Process ... />

    </ProcessMonitor>

  </SystemMonitor>

.. include:: attributes/poll.rst
  
See :doc:`performance`.

Process
-------

.. code:: xml

  <Process name="Browser" watchChildren="false" minCPU="1%"
    onIdle="stop" onStart="" onStop="">

    chrome|edge|firefox

  </Process>

name
++++

You can provide any logical name here, to describe the process or group of processes. This name will be used to represent these processes in the log.

text
++++

:🔍 regex:

The text node of the ``<Process>`` will be parsed as a regular expression and matched against the name of the process.

If the expression contains a path separator — a ``/``, or a ``\\`` matching a literal backslash — it is matched against the full path of the executable instead. Nothing else changes the choice: a ``.`` is read as the regular-expression wildcard it is, so ``chrome.*`` still matches by name.

.. code:: xml

  <Process name="Browser">chrome|firefox</Process>
  <Process name="Games">/usr/games/.*</Process>

watchChildren
+++++++++++++

:default: ``false``

By default, this process group will only include processes with a matching image name. However, if you set ``watchChildren``, their spawned child processes will also be included. However, each individual process will only be included once. Therefore, you can safely set this for processes that spawn child processes of themselves (e.g. most of the web browsers).

.. include:: attributes/cpu.rst

.. include:: attributes/io.rst

.. include:: attributes/traffic.rst

onIdle
++++++

:⚡️ event:

This event is triggered if the configured thresholds (``minCPU``, ``minIO``, ``minTraffic``) were not all met since the last timeout. If no threshold is configured, this event will not be triggered.

onStart
+++++++

:⚡️ event:

This event is triggered when the first process of this group starts.

onStop
++++++

:⚡️ event:

This event is triggered when the last process of this group exits. 