IO
==

``IO`` measures storage reads and writes by the watched processes.
It is available on Windows, Linux, and macOS when ``minIO`` is
configured. Network transfers are measured separately by :doc:`traffic`.

.. include:: /modules/process/attributes/io.rst

Example
-------

This fragment belongs inside ``ProcessMonitor``. The backup process
and its children count as active while their combined storage activity
exceeds the configured threshold:

.. code:: xml

   <Process name="Backup" watchChildren="true" minIO="100kb/s" watch="IO">
     backup
   </Process>

Cached reads and buffered writes are counted differently across platforms.
Calibrate the threshold using the activity reported on the target machine.
See :doc:`watch` for combining metrics.

