Available actions
=================

You can use the following actions to automate the lifecycle of Duo instances. 

.. note::

    You may also use the actions of the :doc:`SystemMonitor </modules/system/actions>` here. If you use ``exec`` to start a program or execute a script when an instance starts, the process will be executed in the context of the user session. However, if you configure ``exec`` when the instance is stopped, it will run in the context of the ``SYSTEM`` account.

start
-----

:🔥 action:

This action attempts to start the instance, so that a remote Moonlight client can connect to it.

stop
----

:🔥 action:

This action attempts to stop the instance. You should use it when the instance has become idle, otherwise every still-connected Moonlight client will be disconnected.