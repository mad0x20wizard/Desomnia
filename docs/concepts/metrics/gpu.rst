GPU
===

:OS: 🪟 *Windows* 🍎 *macOS*

``GPU`` measures graphics-processing time consumed by the watched
processes. It requires ``minGPU`` and supported hardware and platform
counters.

.. include:: /modules/process/attributes/mingpu.rst

Example
-------

This fragment belongs inside ``ProcessMonitor``. CPU or GPU work can
keep the renderer active:

.. code:: xml

   <Process name="Renderer" watchChildren="true"
            minCPU="5%" minGPU="10ms" watch="CPU or GPU">
     blender
   </Process>

Both measurements must be available, even when CPU usage alone satisfies
the :doc:`watch expression <watch>`.

