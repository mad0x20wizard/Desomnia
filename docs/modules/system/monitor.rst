System Monitor
==============

:OS: 🪐 *Platform-independent*

The System Monitor contains the activity monitors and system actions. It is the root of a simple configuration, or the configuration assembled by :doc:`environment rules </concepts/environments>`. Its ``timeout`` sets the inspection interval: during each check, the nested monitors report current or recent activity, and the system is idle when none reports usage. The :doc:`available actions <actions>` include ``sleep``, ``sleepless``, ``reboot``, and ``shutdown``, plus ``exec`` on Windows. Nested monitors can use these system actions as well.

The System Monitor coordinates the other monitors but does not observe anything on its own. The :doc:`configuration reference <config>` covers the global event handlers (``onIdle``, ``onUsage``, ``onSuspend``, ``onSuspendTimeout``, ``onResume``), the ``timeout`` attribute, and keeping the display awake.

.. toctree::
   actions
   config
