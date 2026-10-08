Troubleshooting
===============

Start with the service log and :doc:`logging </concepts/logging>`. It should
show whether the configuration loaded, which monitors started, and what was
reported as activity. On packaged Linux installations, use:

.. code:: bash

   systemctl status desomnia
   journalctl -u desomnia -b

For other installation methods, see their logging locations in the
:doc:`installation guides </start>`.

Configuration errors
--------------------

Read the first configuration or migration error, not just the service's restart
message. Check XML syntax, option values, and the declared format version.
When migrating an older file, keep its old version declaration until the
contents have been converted.

An unknown option can be ignored instead of producing an error. If a setting
has no effect, compare its spelling and placement with the current reference.
For example, ``maxLastInputTime`` is the current session option;
``maxIdleTime`` belongs to format 1.

Automatic reload can stop the application when a new configuration cannot be
loaded. Restore a working file or fix the reported error, then restart the
service if necessary. See :doc:`/concepts/version/migration` for backups and
migration policies.

Environment activation
----------------------

Enable ``writeEffectiveXML="effective.xml"`` on ``EnvironmentMonitor``,
using a writable output path different from the input. Check the resulting
settings and the environment names in the log.

All conditions on a block must match. Check subnet membership, power-source
availability, and the exact interface selector. An interface condition without
``@up`` tests presence, not a working connection. SSID conditions are
Windows-only and compare exact, case-sensitive names. Lid conditions do not
match desktops or systems whose lid state is unavailable.

Allow for the configured debounce. Then check priorities, ``onlyIf``,
``onlyIfNot``, and the default block. See :doc:`/concepts/environments`.

Persistent system activity
--------------------------

Read the activity log to identify the resource that still reports usage.
Common causes include:

- A process watch without thresholds: merely running is enough.
- A Windows remote session with ``watchInputRemote="false"``: the connected
  client counts as input activity.
- A display whose power state is unknown: ``preventIdle="enabled"`` still
  counts it as active.
- A nested process watch: its activity can keep a session or Duo instance
  active even when the containing metric expression is false.
- Permanent or timed sleepless mode enabled through the Windows
  :doc:`taskbar companion </plugins/bridge>`.

On macOS, ``pmset -g assertions`` helps identify application power assertions.
On Windows, use ``powercfg /requests``; on Linux, use
``systemd-inhibit --list``. Compare them with your power-request filter rules.

Premature suspension
--------------------

Check that the work is actually monitored and that the system has
``onUsage="sleepless"``. Read the metric values during the workload.
Thresholds are averaged over an inspection interval; short bursts may fall
below the configured rate.

Check custom AND/OR expressions and platform support. On Linux, per-process
network and GPU thresholds are unavailable. macOS GPU/network measurements
depend on counters supported by the running system. A measurement failure is
an error to investigate, not evidence that an application is idle.

Other sources can force sleep independently of Desomnia, including the
operating system's lid-close policy. See :doc:`/concepts/metrics` for tuning.

Display monitoring and control
------------------------------

To keep the screen on, use ``keepDisplayAwake`` with ``sleepless``.
To count a connected screen as activity, use ``DisplayMonitor``.
To disable a screen in software, use ``disabled`` on macOS.

On Windows, screen keep-awake requests need the taskbar companion in the local
console session. Display disabling is unsupported there. Linux's idle
inhibitor does not directly control a desktop's screen-blanking policy.

Monitor/TV hardware may not report its powered-off state. If a screen keeps the
machine awake unexpectedly, inspect its reported identity and state before
changing the rule. See :doc:`/modules/display/config`.

Duo instance lifecycle
----------------------

:OS: 🪟 *Windows*

Configure both ``SessionMonitor`` and ``DuoSessionMonitor``, and set
``onInstanceDemand="start"`` / ``onInstanceIdle="stop"`` as needed.

For packet capture, verify Npcap and the selected interface. For listener mode,
check firewall access and use non-sandboxed instances. Streaming traffic
thresholds are unsupported in listener mode.

For idle problems, inspect input, process, and streaming activity separately.
A client connection, an input timeout, and a running instance are different
signals. Check shared session selectors as well as the instance's own rules.
See :doc:`/plugins/duo/config`.

FRITZ!Box authentication and access
-----------------------------------

Anonymous host discovery does not grant authenticated port control. LAN port
actions require credentials for the addressed router; use its configured name
in the ``fritz://`` URL. VPN discovery is more reliable with credentials.

Finding the router also does not create a VPN, a port-forwarding rule, or
Wake-on-LAN support on the target. Follow :doc:`remote-access` and
:doc:`/plugins/fritzbox` for the complete setup.

For packet capture, interface, and Wake-on-LAN problems, continue with
:doc:`/modules/network/troubleshooting`.
