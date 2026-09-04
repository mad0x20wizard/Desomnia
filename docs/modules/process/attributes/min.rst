watch
+++++

Controls how the configured activity metrics are combined. Metric names and operators are
case-insensitive. ``and`` binds more tightly than ``or``, and parentheses can be used to group
subexpressions.

.. code:: xml

  <Process name="Game" watch="(CPU or GPU) and Traffic"
    minCPU="20%" minGPU="10ms" minTraffic="1MB/s">game</Process>

The process metrics are ``CPU``, ``GPU``, ``IO``, and ``Traffic``. A referenced metric must have
its corresponding threshold configured; otherwise the configuration is rejected. Every configured
metric is collected before the expression is evaluated, so ``and`` and ``or`` do not short-circuit
measurement failures.

A session additionally supplies ``Input`` when input watching is enabled. A Duo instance supplies
``StreamTraffic`` only when it owns a network-service watch.

``AND`` and ``OR`` remain valid complete expressions. They apply their operator to every supplied
metric and both count as demand when no metric is supplied. This preserves the former catch-all
behaviour; the default for a process is ``default AND``.

The literals ``true`` and ``false`` may appear anywhere an operand may appear. As a complete
expression, ``true`` always counts the process group as demanded while a matching process exists,
whereas ``false`` always considers it idle.

Expressions are composed when a process configuration contributes metrics to a session or Duo
instance. A leading ``and`` or ``or`` combines the whole expression with the expression inherited
from the lower layer; each expression is treated as one grouped operand. For example,
``and StreamTraffic`` extends the preceding expression with the Duo stream-traffic metric.

A leading ``default`` marks a fallback expression. Defaults combine only with other defaults and
are replaced in full by the first non-default expression. ``default`` must be the first token. An
empty ``watch`` attribute is invalid.

Setting any ``minCPU``, ``minGPU``, ``minIO``, or ``minTraffic`` threshold to ``0`` keeps that
metric configured and makes its comparison always match; no unit is required for zero. If a
configured metric cannot be read from any matching process, inspection fails instead of silently
treating that metric as idle or demand.
