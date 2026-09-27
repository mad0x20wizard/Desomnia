watch
+++++

:default: ``default AND``

Combines the configured process metrics into an activity expression. By default,
all supplied metrics must report activity. With no thresholds, a matching
running process counts as active.

.. code:: xml

   <Process name="Game" minCPU="20%" minGPU="10ms"
            watch="CPU or GPU">game</Process>

See :doc:`/concepts/metrics/watch` for syntax, available metrics, defaults,
and expression composition.
