Session Monitor
===============

:OS: 🪟 *Windows*

The Session Monitor tracks Windows user sessions, including Remote Desktop.
It can keep the system awake for user input or work performed by the session's
processes, and run actions such as locking, disconnecting, or logging out an
idle session.

Use selectors for everyone, named users, or administrators. Input timeouts,
CPU/GPU activity, storage I/O, and network traffic can be combined to decide
whether a session is active. Nested process watches can protect particular
applications independently.

The monitor is also required by :doc:`Duo integration </plugins/duo/plugin>`,
which associates each streaming instance with its Windows session.

.. toctree::
   :maxdepth: 2

   config
   actions

Format 1 used ``clockTime``, ``clockRemote``, ``clockDisconnected``, and
``maxIdleTime`` for the input settings. See :doc:`/concepts/version/history`
for the current attribute names and automatic migration.
