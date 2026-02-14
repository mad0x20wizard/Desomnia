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
