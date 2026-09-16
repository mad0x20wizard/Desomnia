measureGPU
++++++++++

:macOS: ``automatic``, ``process``, ``coalition``
:default: ``automatic``

Selects how macOS accounts graphics processor time. This is a system setting and changing it
requires restarting the daemon.

``automatic`` first probes the per-process AGX IORegistry counter and falls back to resource
coalitions when that private schema is unavailable. ``process`` requires the AGX counter and
attributes work to the process that submitted it. It provides the most precise process matching,
but the driver may reset its accumulated value when an application releases its GPU resources.

``coalition`` uses the kernel's persistent resource-coalition ledger. It survives GPU resource
release, but every process belonging to one application reports the same shared value. Desomnia
deduplicates that value within a process watch.

.. code:: xml

   <?system ProcessManager:measureGPU="coalition" ?>
