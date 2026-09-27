Watch expressions
=================

The ``watch`` attribute combines the :doc:`available metrics <index>` into a Boolean expression.
A true result counts as activity for the resource being inspected.
The attribute is supported on ``Process`` elements, session selectors
(``Everyone``, ``User``, and ``Administrator``), and Duo ``Instance``
elements.

Syntax
------

Metric names, operators, and Boolean literals are case-insensitive.
Separate names and operators with whitespace. Parentheses group expressions.

``and``
    Both operands must be true.
``or``
    At least one operand must be true.
``true``
    Always true.
``false``
    Always false.

``and`` takes precedence over ``or``. Thus, ``CPU or GPU and Traffic``
means ``CPU or (GPU and Traffic)``. Use ``(CPU or GPU) and Traffic``
when network traffic must accompany either kind of processing.

Negation, comparisons, and arithmetic are not supported. Configure thresholds
on the corresponding attributes, rather than writing expressions such as
``CPU > 5%``. An empty ``watch`` value is invalid.

.. code:: xml

   <Process name="Minesweeper" minCPU="20%" minGPU="10ms" minTraffic="1MB/s"
            watch="(CPU or GPU) and Traffic">minesweeper</Process>

A complete expression may also be a literal. For a process watch,
``watch="true"`` counts a matching running process as active regardless
of its measured usage; ``watch="false"`` makes that process group idle.

Accumulating expressions
------------------------

``AND`` and ``OR`` are also valid complete expressions:

``watch="AND"``
    Requires every supplied metric to be true.
``watch="OR"``
    Requires at least one supplied metric to be true.

With no supplied metrics, both expressions evaluate to true. Consequently,
a process watch with no thresholds counts a matching running process as
activity.

The default expression is ``default AND`` for processes and
``default OR`` for sessions. A default expression is With multiple process
thresholds, specify ``watch="OR"`` or a named expression when either
measurement should count independently.

Expression composition
----------------------

When several configurations apply to the same session or instance, their
expressions are applied in order. Session selectors apply in this order:
``Everyone``, matching ``User`` entries in document order, then
``Administrator``. Duo instance settings are applied afterward.

An expression without a leading operator replaces the preceding expression.
A leading ``and`` or ``or`` combines the preceding expression with the
entire new expression. Each expression is treated as one grouped operand.

For example, a preceding ``CPU or Input`` followed by
``and StreamTraffic or GPU`` produces
``(CPU or Input) and (StreamTraffic or GPU)``.

A configuration example for a session:

.. code:: xml

   <SessionMonitor>
     <Everyone minCPU="5%" watch="CPU" />
     <User name="^Smith$" watchInput="true"
           maxLastInputTime="10min" watch="or Input" />
   </SessionMonitor>

The selected user's effective expression is ``CPU or Input``.

Default expressions
-------------------

The optional leading ``default`` marks an expression as a fallback.
It must be the first token, before any leading ``and`` or ``or``.

- A non-default expression replaces a preceding default completely, even
  when it starts with ``and`` or ``or``.
- A later default does not change an existing non-default expression.
- Between defaults, a leading operator combines the expressions;
  without one, the later default replaces the earlier one.

For example, ``and CPU`` following the built-in ``default OR``
becomes ``CPU``, rather than ``OR and CPU``. Explicitly name the
metrics to retain when replacing a default.

Nested resources
----------------

A nested ``Process`` (or any other resource) is watched independently and will
keeps their containing session active, even when the
container's expression evaluates to false.

.. code:: xml

   <User name="Neo" watch="Input" maxLastInputTime="10min">
     <Process name="Backup" minIO="100kb/s">backup</Process>
   </User>

This session is considered active on recent input or while the nested backup
process exceeds its storage threshold. The nested process does not explicitly need to express watching the ``IO``
metric, since this is included in it's ``default AND``expression.

Validation
----------

Every referenced metric must be available and measured. For example, ``CPU or GPU``
requires both ``minCPU`` and ``minGPU``; referencing ``Input``
with ``watchInput="false"`` is invalid.

All configured metrics are collected before evaluating the expression.
Boolean operators do not bypass missing metrics or measurement failures:
even ``true or GPU`` requires a valid ``GPU`` metric.
A ``0`` threshold is valid to keep a measurement enabled.
