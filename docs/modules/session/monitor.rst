Session Monitor
===============

:OS: 🪟 *Windows*

The Session Monitor tracks Windows user sessions and uses their activity to determine whether the system is in use. It can target specific users by name, members of the administrators group, arbitrary security groups, or every logged-in account at once. Multiple selectors can overlap: if a session is matched by more than one, it collects the event handlers and merges the attributes from all of them.

Within each selector, individual session events are exposed as configurable event handlers: ``onLogin``, ``onLogout``, ``onDisconnect``, and others. If a session has been idle longer than ``maxIdleTime``, the ``onIdle`` event fires. The :doc:`available actions <actions>` — ``lock``, ``disconnect``, and ``logout`` — operate on the matched session directly. Nested ``<Process>`` elements allow per-session process tracking, so a session is only considered idle once both input activity and the session's own processes are quiet.

The :doc:`configuration reference <config>` lists all selectors, their attributes, and the session-level options that control clock behaviour for remote and disconnected sessions.

.. toctree::
   actions
   config
