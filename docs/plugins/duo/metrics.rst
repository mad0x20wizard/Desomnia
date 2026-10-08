Metrics
=======

The Duo Stream Integration plugin provides the following metric in addition
to the shared :doc:`observable metrics </concepts/metrics>`.

.. _duo-stream-traffic:

StreamTraffic
-------------

:OS: 🪟 *Windows*

``StreamTraffic`` reports activity at a Duo instance's streaming service.
It is available only on an ``Instance`` that has a network-service watch.
It differs from :doc:`/concepts/metrics/traffic`, which measures transfers attributed to the
session's processes.

watchStreamTraffic
++++++++++++++++++

:default: ``true``

Enables the metric on ``DuoSessionMonitor``; individual instances can
override this setting. Disabling it removes ``StreamTraffic`` from the
available expression operands. Session and process metrics remain available,
and incoming requests can still start an instance.

minStreamTraffic
++++++++++++++++

:inherited:

Overrides the streaming-service threshold for one ``Instance``.
Without a threshold, activity follows the service's normal connection
detection.

Streaming thresholds require packet capture on the interface carrying the
client's traffic. Listener mode detects connections but rejects streaming
thresholds. See :doc:`/plugins/duo/demand`.

Formats
+++++++

Both threshold attributes accept the following network-traffic formats:

.. include:: /modules/network/attributes/traffic.rst
   :start-after: The following formats are valid:

Example
+++++++

While using packet capture, you can use this fragment inside
``DuoSessionMonitor``, to have recent user input or streaming traffic 
keep the instance active:

.. code:: xml

   <Instance name="Neo" watch="Input or StreamTraffic"
             maxLastInputTime="10min"
             minStreamTraffic="1MB/s" />

Replace ``Neo`` with the internal instance name from Duo. The streaming
threshold measures average traffic during the inspection interval.
See :doc:`/concepts/metrics/watch` for expression syntax and composition.
