watchInput
++++++++++

:default: ``true``

Includes input activity in session inspection. This setting
can be specified on the monitor or overridden by a selector. When disabled,
the ``Input`` metric is unavailable; do not reference it in a watch expression.
Other metrics and nested process watches remain available.

watchInputRemote
++++++++++++++++

:default: ``false``

When false, an attached remote client counts as input
activity regardless of how long ago the last input occurred. When true, remote
sessions are subject to the input timeout as well.

Duo instances enable remote input tracking by default; see
:doc:`/plugins/duo/config`.

watchInputDisconnected
++++++++++++++++++++++

:default: ``false``

When true, recent input can still count after the session
disconnects. When false, a disconnected session contributes no input activity.
Its process activity can still keep it active.

maxLastInputTime
++++++++++++++++

:⏱️ duration:
:default: *inspection interval*

The maximum age of the last input that counts as activity. An explicit duration,
such as ``10min``, sets an independent input timeout without changing the
inspection interval.

These settings are inherited from ``SessionMonitor`` by each selector.
