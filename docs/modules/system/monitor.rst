System Monitor
==============

:OS: 🪐 *Platform-independent*

The System Monitor is the root element of every Desomnia configuration. It owns the global idle ``timeout`` — the period during which all nested monitors must report no activity before the ``onIdle`` event fires. Because the timeout is driven by a periodic check rather than a precise timer, every monitor in the hierarchy evaluates against the same clock. All :doc:`available actions <actions>` — ``sleep``, ``sleepless``, ``restart``, ``shutdown``, and ``exec`` — are defined at this level and inherited throughout the configuration, so any event handler anywhere in the tree can reference them.

The System Monitor coordinates the other monitors but does not observe anything on its own. The :doc:`configuration reference <config>` covers the global event handlers (``onIdle``, ``onDemand``, ``onSuspend``, ``onSuspendTimeout``, ``onResume``) and the ``timeout`` attribute.

.. toctree::
   actions
   config
