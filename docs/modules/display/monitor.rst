Display Monitor
===============

:OS: 🪟 *Windows* 🍎 *macOS*

The Display Monitor reports display activity and triggers actions on connection,
power-state, and laptop lid changes.

.. toctree::
   :maxdepth: 2

   config

Display activity
----------------

This example keeps the system awake while an external display is enabled, or
while an application has a power request. It sleeps when neither reports usage:

.. code:: xml

   <SystemMonitor version="3" timeout="2min" onUsage="sleepless" onIdle="sleep">
     <DisplayMonitor preventIdle="enabled" />
     <PowerRequestMonitor />
   </SystemMonitor>

With no display selectors, ``DisplayMonitor`` watches all external displays.
The built-in laptop panel is selected separately.

A display whose power state cannot be determined still counts as enabled.
Some monitors and televisions keep their connection alive when switched off,
so test how your equipment reports its state before relying on it for sleep.

Lid events
----------

.. code:: xml

   <SystemMonitor version="3" timeout="5min" onUsage="sleepless" onIdle="sleep">
     <PowerRequestMonitor />
     <DisplayMonitor>
       <DisplayBuiltIn onLidClose="sleep+30s" />
     </DisplayMonitor>
   </SystemMonitor>

The built-in panel does not count as activity by default. This configuration
schedules sleep after closing the lid;
opening it again cancels the pending lid-close action. The operating system's
own lid-close policy still applies and may suspend the laptop sooner.

Use :doc:`environments </concepts/environments>` to make lid-dependent settings
conditional on mains power or a dock connection.

Display disabling
-----------------

:OS: 🍎 *macOS*

``disabled="true"`` holds a selected display disconnected in
software while that configuration applies. For example, an environment can
disable the built-in panel while using an external screen. Displays are
released when the setting no longer applies or Desomnia stops.

Windows supports monitoring displays but does not support this software
disconnect feature.

This is separate from :ref:`keep-display-awake`, which asks the system to
keep the screen on while Desomnia is holding a sleepless request.

See :doc:`/guides/troubleshooting` for unexpected display activity.
