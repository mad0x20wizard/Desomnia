Interactive Taskbar Icon
========================

:OS: 🪟 *Windows-only*

The optional taskbar companion gives the logged-in user access to Desomnia's
sleepless controls and activity information. Install **Interactive Taskbar Icon**
through the Windows installer; rerun the installer in Modify mode if needed.

Permissions
-----------

All user controls are **denied by default**, including for administrators.
Installing the companion grants no permissions. Enable each capability explicitly
on an ``Everyone``, ``User``, or ``Administrator`` selector under ``SessionMonitor``:

.. code:: xml

   <SystemMonitor version="3">
     <SessionMonitor>
       <User name="^Alice$"
             allowControlSleep="true"
             allowControlInspection="true"
             allowControlSession="self" />
     </SessionMonitor>
   </SystemMonitor>

``allowControlSleep`` allows manual keep-awake settings and immediate system sleep.
``allowControlInspection`` separately allows requesting an inspection and receiving
machine-wide activity tokens. With inspection permission alone, **Show activity**
opens the dialog with sleep controls disabled. Without inspection permission, the
whole lower activity section is hidden and no usage tokens are sent to the companion.
Changing the activity-based keep-awake checkbox requires both permissions.

``allowControlSession`` allows session disconnection and console switching.
``self`` means only the requesting session; a regular expression selects target
user names, and ``true`` or ``*`` selects all targets. Switching the console also
requires permission for the session being displaced. Session details are limited
to the caller's own session and permitted targets.

Selectors apply in order: ``Everyone``, matching ``User`` entries, then
``Administrator``. Later explicit boolean values replace earlier values. Session
selectors accumulate; ``allowControlSession="false"`` clears previous grants.
The service rechecks permissions for each command. Restart the service after
changing these settings so existing sessions receive their updated permissions.

Language
--------

The companion uses German for German Windows display-language settings (including
Germany, Austria and Switzerland), and English otherwise. Language is selected
per signed-in user. Date and time formatting follows that user's regional settings.
Translations use standard .NET ``.resx`` resources; the installer includes the
German satellite resource assembly alongside the English fallback resources.

Sleepless controls
------------------

Use the icon's configuration dialog to keep the computer awake permanently,
until a chosen time, or according to monitored activity. Timed mode releases
the manual hold at the selected time (up to 24 days ahead); permanent mode has
no deadline. Activity-based mode uses the resources
reported by the service; it is not just a keyboard/mouse idle timer.

The dialog shows the activity reported by the monitors, which helps explain
why Desomnia considers the system in use. A permanent or timed manual hold can
keep the machine awake even when ordinary activity monitors are idle.

Left-click toggles the manual keep-awake setting when sleep control is permitted;
double-click requests immediate sleep. Right-click opens the available commands.
For inspection-only users, left-click opens the activity dialog.

Deployment and troubleshooting
------------------------------

The bridge runs in the LocalSystem service and launches an unelevated companion in
each configured user session. Keep service binaries, plugins and the service
configuration in administrator-controlled locations. Companion logs are stored in
``%LOCALAPPDATA%\Desomnia\logs``. ``spawnMinions="false"`` on ``SessionMonitor``
disables companions.

The versioned pipe protocol requires upgrading the bridge and companion together;
older binaries cannot connect. The companion verifies its service endpoint, and
the service restricts each pipe to the intended Windows logon session and launched
process. A failed companion is restarted with increasing delays, up to five times
per session during the service lifetime.

Display power requests
----------------------

The companion also makes display keep-awake requests on behalf of the service
in the local console session. Enable :ref:`keep-display-awake` if the screen
should stay on while Desomnia holds its sleepless request. With no console
session there is no local display request to hold.

The setting belongs in the system configuration; installing the icon alone
does not enable it. See :doc:`/guides/troubleshooting` for display and
keep-awake problems.
