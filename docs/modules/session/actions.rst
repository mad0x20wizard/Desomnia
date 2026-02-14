Available actions
=================

These actions are valid for every watched session, regardless if they were selected by ``<User>``, ``<Administrator>``, ``<Group>`` or ``<Everyone>``.

lock
----

:🔥 action:

This action causes the session to lock itself (if supported), so that it has to be unlocked again with the account password.


disconnect
----------

:🔥 action:

This action causes the session to disconnect. This will terminate any Remote Desktop (RDP) connection and send any local user back to the login screen.

logout
------

:🔥 action:

This action causes the session to logout.

.. warning::

    All running processes will be terminated and unsaved data will be lost.