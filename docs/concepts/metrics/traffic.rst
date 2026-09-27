Traffic
=======

:OS: 🪟 *Windows* 🍎 *macOS*

``Traffic`` measures network transfers attributed to the watched
processes. It requires ``minTraffic``. It is independent of the
streaming-service measurement supplied by :ref:`StreamTraffic <duo-stream-traffic>`.

.. include:: /modules/process/attributes/traffic.rst

Example
-------

This fragment belongs inside ``ProcessMonitor``. The downloader counts
as active while its average network traffic exceeds the configured threshold:

.. code:: xml

   <Process name="Downloader" minTraffic="100kb/s" watch="Traffic">
     curl
   </Process>

On Linux, use :doc:`network-service monitoring </modules/network/monitor>`
or a supported process metric. See :doc:`watch` for combining metrics.
