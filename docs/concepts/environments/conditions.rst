Conditions
==========

Conditions are attributes on ``<Environment>``. All conditions on an environment
must match before its settings can apply. ``onlyIf`` and ``onlyIfNot`` impose
additional dependencies; see :doc:`config`.

Several environments may match at the same time. Alternatives are expressed
as separate environments, whose settings are combined according to the
:doc:`merging` rules. An environment without conditions matches unconditionally,
subject to its dependencies.

The core and monitoring modules supply the following condition groups. Platform
annotations identify where each condition is supported.

.. toctree::
   :maxdepth: 2

   conditions/power
   conditions/network
   conditions/display
   conditions/variables
