Input
=====

:OS: 🪟 *Windows*

``Input`` reports recent input in a user session. It is available on
:doc:`session selectors </modules/session/config>` when ``watchInput`` is enabled.
It is not available on a ``Process`` element.

The following attributes control the metric:

.. include:: /modules/session/options/clock.rst

Example
-------

This configuration combines recent input with storage activity across all
processes in the selected session:

.. code:: xml

   <SystemMonitor version="3" timeout="2min" onUsage="sleepless" onIdle="sleep">
     <SessionMonitor>
       <User name="^Smith$" watchInput="true" watchInputRemote="true"
             maxLastInputTime="10min" minIO="100kb/s" watch="Input or IO" />
     </SessionMonitor>
   </SystemMonitor>

The session counts as active after recent input or sufficient storage activity.
The ten-minute input timeout is checked during regular inspection; it does
not change the two-minute inspection interval.

A nested process provides an independent activity signal:

.. code:: xml

   <User name="^Smith$" maxLastInputTime="10min">
     <Process name="Backup" minIO="100kb/s">backup</Process>
   </User>

Here, a busy backup process keeps the session active even when the session's
own input metric is false. See :doc:`watch`.

