Power Supply
============

:OS: 🪟 *Windows* 🐧 *Linux* 🍎 *macOS*

Power-supply conditions select settings according to the system's current
power source.

power
-----

Matches the power source:

``ac``
    The system is connected to mains power.
``battery``
    The system is running on battery power.

If the power source cannot be determined, neither value matches and a warning
is logged.

.. rubric:: Example

This configuration selects a shorter inspection interval on battery power:

.. code:: xml

   <EnvironmentMonitor version="2">
     <DefaultEnvironment>
       <SystemMonitor timeout="5min" onUsage="sleepless" onIdle="sleep">
         <PowerRequestMonitor />
       </SystemMonitor>
     </DefaultEnvironment>

     <Environment power="battery">
       <SystemMonitor timeout="2min" />
     </Environment>
   </EnvironmentMonitor>

The power-request monitor and sleep actions apply under either power source.
The inspection interval is two minutes on battery and five minutes on mains
power. Additional activity monitors belong in the default environment; see
:doc:`/guides/sleep`.
