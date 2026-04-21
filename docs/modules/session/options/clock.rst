clockTime
+++++++++

:inherited:
:default: ``true``

This controls if the last input time should be tracked at all for the session. If at least on matching session config sets this to ``false``, the last input time tracking will be disabled for the session.

This controls if the time of the last input should be considered if the session is remote connected. If ``clockRemote`` is disabled, a connected remote client will never be considered idle.

clockRemote
+++++++++++

:inherited:
:default: ``false``

This controls if the time of the last input should be considered if the session is remote connected. If ``clockRemote`` is disabled, a connected remote client will never be considered idle.

clockDisconnected
+++++++++++++++++

:inherited:
:default: ``false``

This controls if the time of the last input should be considered if the session is disconnected. If ``clockDisconnected`` is disabled, a disconnected session will always be considered idle.
