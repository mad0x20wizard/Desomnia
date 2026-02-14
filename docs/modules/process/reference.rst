Configuration reference
=======================

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

Process
-------

.. code:: xml

  <Process name="Browser" withChildren="false" minCPU="1%" 
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

watchChildren
+++++++++++++

:default: ``false``

By default, this process group will only include processes with a matching image name. However, if you set ``watchChildren``, their spawned child processes will also be included. However, each individual process will only be included once. Therefore, you can safely set this for processes that spawn child processes of themselves (e.g. most of the web browsers).

.. include:: attributes/cpu.rst

onIdle
++++++

:⚡️ event:

This event is triggered if the processing time used since the last timeout is less than the configured ``minCPU`` value. If no CPU threshold is configured, this event will not be triggered.

onStart
+++++++

:⚡️ event:

This event is triggered when the first process of this group starts.

onStop
++++++

:⚡️ event:

This event is triggered when the last process of this group exists 