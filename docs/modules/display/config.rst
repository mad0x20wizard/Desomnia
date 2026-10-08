Configuration
=============

DisplayMonitor
--------------

:OS: 🪟 *Windows* 🍎 *macOS*

.. code:: xml

   <DisplayMonitor preventIdle="enabled" debounceTime="5s">
     <Display name="DELL.*" preventIdle="always" />
     <DisplayBuiltIn preventIdle="never" />
   </DisplayMonitor>

Without selectors, all external displays are watched. A configuration containing
only ``DisplayBuiltIn`` selects only the built-in panel. An additional empty
``<Display />`` selects all external displays.

preventIdle
+++++++++++

:default: ``enabled``

Controls whether external displays contribute activity during inspection:

- ``enabled``: active unless the display
  is known to be powered off.
- ``always``: active while connected, regardless of power state.
- ``never``: watch events without contributing activity.

``true`` is an alias for ``always``, and ``false`` for ``never``.
A display-level setting overrides the monitor default.

disabled
++++++++

:default: ``false``
:OS: 🍎 *macOS*

Holds selected displays disconnected in software.
An external display can override this monitor-level default.
On Windows, attempting to disable a display logs an unsupported-operation
warning.

debounceTime
++++++++++++

:⏱️ duration:
:default: ``5s``

The grace period for a display to reconnect before its removal
is treated as a disconnect. This avoids triggering disconnect actions for
brief interruptions during mode changes or link negotiation.

onUsage
+++++++

:⚡️ event:

Triggered on every inspection that reports display activity. Periodic inspection
requires ``timeout`` on ``SystemMonitor``.

onIdle
++++++

:⚡️ event:

Triggered when all monitored displays are idle.

Display
-------

Selects external displays. All supplied criteria must match; an empty
``<Display />`` matches every external display.

Connection and power events support :doc:`delayed actions </concepts/resources>`.

name
++++

:🔍 regex:

A regular expression matched against the display's reported model name.
``*`` selects any name. This is a hardware model name, not a user-defined
label for the rule.

vendor
++++++

The manufacturer code, for example ``DEL``. Matched case-insensitively.

product
+++++++

The numeric product code.

serial
++++++

The reported serial string or numeric serial written in decimal. Serial strings
are matched case-insensitively.

resolution
++++++++++

The exact native resolution, for example ``3840x2160``. The current desktop
resolution does not affect this criterion.

minWidth
++++++++

The minimum native width in pixels.

minHeight
+++++++++

The minimum native height in pixels.

preventIdle
+++++++++++

:default: *inherited from DisplayMonitor*

Controls whether this display contributes activity. Accepts the same values
as the monitor-level setting.

disabled
++++++++

:default: *inherited from DisplayMonitor*
:OS: 🍎 *macOS*

Holds this display disconnected in software while the configuration applies.

onConnect
+++++++++

:⚡️ event:

Triggered when the display connects.

onDisconnect
++++++++++++

:⚡️ event:

Triggered when the display disconnects after the configured grace period.

onPowerOn
+++++++++

:⚡️ event:

Triggered when the platform reports that the display is powered on.

onPowerOff
++++++++++

:⚡️ event:

Triggered when the platform reports that the display is powered off.

DisplayBuiltIn
--------------

Selects the built-in panel. There are no name or hardware-selection attributes.
Lid events support delayed actions; the opposite event cancels a pending action.

preventIdle
+++++++++++

:default: ``never``

Controls whether the built-in panel contributes activity. Accepts the same
values as ``DisplayMonitor``, but does not inherit its default.

disabled
++++++++

:default: ``false``
:OS: 🍎 *macOS*

Holds the built-in panel disconnected in software. Does not inherit the
monitor-level setting.

onLidOpen
+++++++++

:⚡️ event:

Triggered when the laptop lid opens.

onLidClose
++++++++++

:⚡️ event:

Triggered when the laptop lid closes.
