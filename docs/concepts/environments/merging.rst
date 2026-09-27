.. _environment-merging:

Merging
=======

Matching environments contribute to a single effective ``SystemMonitor``
configuration. Activation and precedence are separate: ``priority`` resolves
conflicting values but does not prevent other matching environments from
contributing settings.

Environment selection
---------------------

An environment applies when all its conditions and dependencies match.
``onlyIf="never"`` excludes an environment. A ``DefaultEnvironment`` normally
applies alongside all matching environments; ``onlyIf="else"`` restricts it
to cases where no ordinary environment applies.

Active environments are merged in document order, including the default
environment at its position in the file. If none applies, the resulting
configuration supplies no activity monitors or sleep actions.

Element matching
----------------

Elements with the same type and ``name`` are merged. Their attributes and
children are combined recursively. For example, a named ``NetworkMonitor``
can define services in one environment and receive additional settings in
another.

Unnamed collection items, such as multiple ``NetworkMonitor`` elements, remain
separate. Matching names are required to modify the same collection item across
environments. Single elements such as ``SystemMonitor`` merge without a name.

Settings omitted from a later environment remain in place. An empty attribute
or element does not erase an existing nonempty value. Mutually exclusive
configurations require conditions or dependencies such as ``onlyIfNot``.

Value conflicts
---------------

When two active environments assign different values to the same setting,
the value from the environment with the higher
:ref:`priority <environment-priority>` takes precedence, regardless of document
order. Identical values do not constitute a conflict.

For equal priorities, the :ref:`conflictStrategy <environment-conflict-strategy>`
attribute on ``EnvironmentMonitor`` determines the result.

Example
-------

This configuration combines shared monitoring with a battery-specific timeout:

.. code:: xml

   <EnvironmentMonitor version="2" conflictStrategy="error"
                       writeEffectiveXML="effective.xml">

     <DefaultEnvironment>
       <SystemMonitor timeout="5min" onUsage="sleepless" onIdle="sleep">
         <PowerRequestMonitor />
       </SystemMonitor>
     </DefaultEnvironment>

     <Environment power="battery" priority="10">
       <SystemMonitor timeout="2min" />
     </Environment>
   </EnvironmentMonitor>

On battery, the higher priority selects ``timeout="2min"`` even with
``conflictStrategy="error"``. The other attributes and the power-request
monitor remain in place. The effective XML is equivalent to:

.. code:: xml

   <SystemMonitor version="2" timeout="2min" onUsage="sleepless" onIdle="sleep">
     <PowerRequestMonitor />
   </SystemMonitor>

On mains power, only the default environment applies and the timeout is five
minutes.

Effective configuration
-----------------------

The output attributes on ``EnvironmentMonitor`` expose the result of merging:

``writeEffectiveXML``
    Writes the combined configuration as readable XML, including its format
    version. This shows which monitors, attributes, and actions apply.

``writeEffectiveConfiguration``
    Writes the combined settings as a flat list of keys and values. This is
    useful for inspecting the resolved values and collection entries.

Outputs are written for the initial configuration and updated when the effective
configuration changes. Relative paths are resolved beside the source file.
The service account requires write access; output paths must differ from the
source configuration and from each other.

These files are generated output. Changes belong in the source environment
configuration. The files are removed when Desomnia stops. Copy an output file before
stopping the service if it is needed for later inspection.

The service log also identifies the active environments; see
:doc:`/guides/troubleshooting` for activation and configuration errors.
