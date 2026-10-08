Configuration migration
=======================

By default, Desomnia converts older settings each time it loads a configuration,
without modifying the file on disk. Persistent migration saves the converted
configuration to the source file.

Persistent migration
--------------------

Add the following processing instruction after the XML declaration and before
the outermost element. **Leave the existing format version unchanged** for this
first run:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <?config autoMigrate="transient|persistent" writeBackupXML="monitor.v?.xml"?>
   <SystemMonitor version="1" timeout="5min" onDemand="sleepless" onIdle="sleep">
     <PowerRequestMonitor />
   </SystemMonitor>

When this example is loaded, Desomnia applies all automatic migrations to the latest format version,
and saves the file. Before replacing it, Desomnia copies the
original to ``monitor.v1.xml`` beside it. A comment in the updated file records
the conversion.

If a migration produces a warning, this policy continues with the converted
settings in memory but does not save the affected step. Review the log and
resolve migration warnings before considering the saved file fully converted.

Configuration
-------------

The optional ``<?config ...?>`` instruction belongs before
``<SystemMonitor>`` or ``<EnvironmentMonitor>``, and before any
``<?system ...?>`` instructions. Use it only once.

autoMigrate
+++++++++++

:default: ``transient``

Controls whether older settings are converted and whether the result is saved:

``transient``
    Converts settings in memory and leaves the source file unchanged. Migration
    messages recur on subsequent starts or reloads until the file is updated.

``transient|persistent``
    Saves migration steps that complete without warnings. Continues in memory
    if a step needs review or the file cannot be written.

``persistent``
    Saves each migration step that completes without warnings. Stops if a step
    needs review or cannot be written, ensuring that the running configuration
    matches the saved file.

``never``
    Disables conversion. A configuration too old for the installation is
    rejected. ``none`` is an alias; neither value can be combined with another
    policy.

writeBackupXML
++++++++++++++

The backup path for the original configuration before a persistent migration.
The service account requires write access to both the configuration and backup.
The backup path must differ from the source path.

Relative paths are resolved beside the configuration file. A ``?`` in the name
is replaced by the old format version. Without ``?``, a later backup replaces
the earlier backup at that path.

version
+++++++

An alternative to the ``version`` attribute on the outermost element, for
example ``<?config version="3"?>``. If both locations declare a version, the
numbers must agree. See :doc:`/concepts/version`.

Migration messages
------------------

Each migration message identifies the affected element or attribute:

- **INFO:** an automatic change, such as renaming an option.
- **WARN:** review the change before saving it. The initial announcement that
  migration is starting is also logged at this level.
- **ERROR:** the conversion cannot be completed; fix the configuration before
  starting Desomnia again.

Migration handles known old settings, not every possible typo or custom
configuration. Unknown options may be ignored, so compare custom settings with
the current module reference if they appear to have no effect.

Output format
-------------

The format version is updated, and existing comments and layout are largely
preserved. Attribute formatting can change when an element is rewritten. A
backup is therefore useful even when all changes are automatic.

See :doc:`history` for the changes made by each format version and
:doc:`/guides/troubleshooting` if a configuration fails to load.
