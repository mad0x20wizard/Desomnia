Configuration
=============

EnvironmentMonitor
------------------

The outermost element of an environment configuration. Contains ``Environment``
elements and, optionally, one ``DefaultEnvironment``:

.. code:: xml
   :force:

   <EnvironmentMonitor version="3" debounce="3s" conflictStrategy="last"
                       writeEffectiveXML="effective.xml"
                       writeEffectiveConfiguration="effective.txt">
     <DefaultEnvironment ... />
     <Environment ... />
   </EnvironmentMonitor>

version
+++++++

The configuration format version. See :doc:`/concepts/version`.

debounce
++++++++

:⏱️ duration:
:default: ``3s``

The delay after a condition changes before the active configuration is
recalculated. Zero disables the delay.

.. _environment-conflict-strategy:

conflictStrategy
++++++++++++++++

:default: ``last``

Controls conflicts between active environments with equal priority:

``last``
    Uses the value from the later environment in the file and logs a warning.
``first``
    Keeps the value from the earlier environment and logs a warning.
``error``
    Rejects conflicting values. The diagnostic identifies the setting and
    the contributing environments.

Values from higher-priority environments take precedence regardless of this
setting. See :ref:`environment-merging`.

writeEffectiveXML
+++++++++++++++++

An optional output path for the combined XML configuration, including its
format version. Relative paths are resolved beside the source configuration.
The path must differ from the source path, and the service account requires
write access. See :doc:`merging` for an example of the exported configuration.

writeEffectiveConfiguration
+++++++++++++++++++++++++++

An optional output path for the combined settings as a flat list of keys and
values. Relative paths are resolved beside the source configuration, and the
service account requires write access. See :doc:`merging` for output details.

Environment
-----------

Contains a ``SystemMonitor`` element, or the monitors that would go inside one.
An explicit ``SystemMonitor`` must be the environment's only direct child:

.. code:: xml
   :force:

   <Environment name="Battery" priority="10" onlyIf="always" power="battery">
     <SystemMonitor timeout="2min">
       <!-- additional monitor settings -->
     </SystemMonitor>
   </Environment>

   <Environment name="Home" onlyIfNot="Battery" network="192.168.178.0/24">
     <NetworkMonitor name="Home network" ... />
   </Environment>

The attributes below control identity, precedence, and dependencies. See
:doc:`conditions` for the additional attributes that select environments
by various observable system states.

name
++++

An optional name, used in logs and references from other environments.
When omitted, the display name is generated from the condition attributes and
their values. For example, an environment with ``power="battery"`` is displayed
as ``power="battery"``; multiple conditions produce a name such as
``lid="closed" power="ac"``.

An explicit name is therefore unnecessary when the conditions already provide
a sufficient description. It is required when another environment references
this one through ``onlyIf`` or ``onlyIfNot``; generated display names cannot
be used for these references.

An unnamed environment without conditions receives a numbered display name,
such as ``anonymous #1``. The explicit names ``always``, ``never``, and ``else``
are reserved.

.. _environment-priority:

priority
++++++++

:default: ``0``

An integer, including negative values, defining precedence when settings
conflict. Higher values take precedence regardless of document order. For
example, ``-1`` has lower precedence than the default ``0``.
Priority does not stop other matching environments from contributing settings.

onlyIf
++++++

:default: ``always``

Restricts activation to when the named environment also applies, in addition
to this environment's own conditions. The referenced name must exist, and
references cannot form a cycle.

``always`` imposes no additional restriction. ``never`` disables the environment.

onlyIfNot
+++++++++

Prevents activation while the named environment applies. The environment's own
conditions must still match. The referenced name must exist, and references
cannot form a cycle.

DefaultEnvironment
------------------

Contains shared settings. Only one default environment is permitted. It cannot
have conditions or a name:

.. code:: xml

   <DefaultEnvironment priority="0" onlyIf="always">
     <SystemMonitor timeout="5min" onUsage="sleepless" onIdle="sleep">
       <PowerRequestMonitor />
     </SystemMonitor>
   </DefaultEnvironment>

As with ``Environment``, monitor elements may also appear directly inside this
element. If no environment applies, the resulting system configuration is empty
and supplies no activity monitors or sleep actions.

priority
++++++++

:default: ``0``

An integer, including negative values, defining the precedence of these settings
relative to other environments. Higher values take precedence when settings
conflict.

onlyIf
++++++

:default: ``always``

Restricts activation in the same way as on ``Environment``. The additional
value ``else`` applies these settings only when no ordinary environment matches.
By default, the settings apply even when other environments match.

onlyIfNot
+++++++++

Prevents activation while the named environment applies, as on ``Environment``.
