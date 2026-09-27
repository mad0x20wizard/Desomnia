CPU
===

``CPU`` measures processor time consumed by the watched processes.
It is available on all supported platforms when ``minCPU`` is configured.

.. include:: /modules/process/attributes/cpu.rst

Example
-------

This fragment belongs inside ``ProcessMonitor``. The renderer and its
children count as active when their combined CPU usage exceeds five percent
of the machine's total processor capacity during the inspection interval:

.. code:: xml

   <Process name="Renderer" watchChildren="true" minCPU="5%" watch="CPU">
     blender
   </Process>

See :doc:`watch` to combine CPU usage with other metrics.

