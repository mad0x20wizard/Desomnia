min
+++

How the configured thresholds combine. The default is ``and``.

``and`` (default)
    The group counts as active only while it satisfies **every** configured threshold at once. This is the right reading for a group whose work is one kind of work — a backup that is only genuinely busy while it is both computing and writing.

``or``
    **Any** single threshold is enough. This is the right reading for a group whose work moves between the metrics: a game renders hard while computing little, a build computes hard while rendering nothing, and under ``and`` either of them reads as idle in the middle of the work.

.. code:: xml

  <Process name="Game" min="or" minCPU="20%" minGPU="10%">game</Process>

Only the thresholds you actually configure take part; the attribute has no effect where a single one is set.

.. note::

  Where a counter exists but nothing answered for it in a given cycle — every process of the group exited between two cycles, or none of them may be opened — that attribute is left out of the comparison for that cycle rather than counted as unmet. A cycle in which *no* configured threshold could be measured is read as demand, so a momentary measurement gap can never be what puts the machine to sleep. A threshold the platform keeps no counter for at all is a different matter: it is refused when the watch is built, and the service does not start.
