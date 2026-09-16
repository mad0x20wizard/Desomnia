Versioning
======================

A configuration file declares the format version it is written in as a ``version`` attribute on the root element:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <SystemMonitor version="2">
     <!-- ... -->
   </SystemMonitor>

The attribute is a statement the file makes about its own format, not part of the configuration: it is not served to the application. There is exactly one format version for the whole product: it is raised whenever *any* module (or plugin) introduces a change that an older file would not survive unchanged, and this page documents what changed and how an existing configuration migrates. Modules and plugins never carry a version of their own. A file that declares no version at all is a **version 1** file.

The ``<?config?>`` header
-------------------------

Everything else a file can say about its format handling lives in an *optional* ``<?config?>`` header — a processing instruction, like the XML declaration it follows, and like it at the top of the file:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <?config autoMigrate="persistent" writeBackupXML="monitor.v?.xml"?>
   <SystemMonitor version="1">

The header is placed *outside* the root element — before ``<SystemMonitor>`` (or ``<EnvironmentMonitor>``, when the file declares environments), before any ``<?system?>`` directive, right after the XML declaration; only comments may precede it, and there is at most one. Like the version attribute it is neither served nor reloaded.

Most files never need one: automatic (in-memory) migration is the default, so the header is only written to *forbid* the migration (``autoMigrate="never"``), to make it write the file (``persistent``), or to carry the version in header style — ``<?config version="2"?>`` is accepted as an alternative place for the declaration. When both the root element and the header declare a version, the two must match; the root element is the authoritative place, and a migration keeps whichever style the file uses (see below).

Supported and required
----------------------

Every build supports the newest format version it knows, and every module states the oldest version it still reads without migration. Together this gives two numbers: the **supported** version (the newest version every loaded module accepts — normally the current one, unless a strict plugin limits it, which is logged as a warning) and the **required** version (the oldest version that no loaded module needs migrated).

The version check runs against these two numbers *after* the migration had its chance, and independently of it:

* A file **newer** than the supported version is refused — it belongs to a newer version of the software.
* A file **older** than the *required* version is refused only when it was not migrated (``autoMigrate="never"``); the error names the module that demands the newer format.
* A file older than the *supported* version but not older than the *required* one is perfectly fine as it is: as long as no loaded module demands a newer format, an old file is a valid file — nothing is migrated, nothing is logged.

In other words: migration happens only when a loaded module actually requires it, and the version check holds even when migration is disabled.

Automatic migration
-------------------

The migration pipeline sits *underneath* the configuration source — it decorates the file access itself — and runs **in memory, before any other processing**, every time the file is read: at startup and again on every automatic reload, so everything downstream only ever sees the migrated form. It walks the file up one version at a time to the *required* version: for every step it logs one **WARN** line (``Migrating the configuration from version 1 -> 2...``), followed by one line per change, at the severity the module chose::

   WARN  Migrating the configuration from version 1 -> 2...
   INFO  /SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort -> renamed to @watchPort

Every line names the affected node by its XPath in the file, as it was *before* the change, so you can find it. An **INFO** line is a change that needs no attention, a **WARN** line asks you to look at the result, and an **ERROR** line means the change could not be made automatically: the startup is aborted and the file has to be updated by hand (the log has the specifics). A file that needs no migration is read silently. A file that cannot be read or migrated stops the application — at startup and on an automatic reload alike; the error is logged first, and the service manager's restart is the recovery path.

What may happen to an outdated file is decided by the header:

autoMigrate
    ``transient``
        The file is migrated in memory only and stays as it is on disk. **This is the default** — of a file without any header, and of a header that does not say ``autoMigrate`` — so an existing configuration keeps working (and keeps logging the migration on every start, until you update the file).

    ``never``
        An outdated file is refused; update it by hand (or start once with ``autoMigrate="persistent"`` — or ``transient|persistent`` — let Desomnia write the file, and then set it back to ``never``). ``none`` is accepted as an alias.

    ``persistent``
        The migrated document is written back to the file after every migration step that produced no warnings. At the first warning, writing stops: with ``persistent`` alone the application stops as well (the file was not updated, so nothing is lost — review the warnings, update the file, or allow the next option); with ``transient|persistent`` the migration continues in memory and the file stays at the last version that was written without a warning. The file is replaced atomically (written next to it, then moved over it) where the platform allows it, and rewritten in place otherwise, so ``writeBackupXML`` is recommended together with ``persistent``.

    The keywords are case-insensitive and may be combined with ``|`` — except ``never``, which stands alone.

writeBackupXML
    A path where the file is copied *before* it is overwritten (only with ``autoMigrate="persistent"``; otherwise it is ignored). Every ``?`` in the path is replaced by the version of the content being backed up: with ``monitor.v?.xml`` a migration from version 1 to 3 leaves ``monitor.v1.xml`` and ``monitor.v2.xml`` next to the file. A relative path is resolved relative to the configuration file, an absolute path is taken as is; the path must not be the configuration file itself. Without a ``?`` every backup overwrites the same file.

A migrated file has its version stamped into the root element's ``version`` attribute (added when the file declared none) — and into the header, where the user chose to declare it there too, so the file keeps its style — and keeps a **migration protocol** right below the top of the file (before any ``<?system?>`` directive): one dated comment per migration run, listing every step the run wrote and its changes (a step without any is a bare ``Version 2 -> 3.``), so the history stays readable in the file itself. A later run adds its own comment after the earlier ones — as long as you leave them where they are:

.. code:: xml

   <?config autoMigrate="transient|persistent" writeBackupXML="monitor.v?.xml" ?>
   <!-- MIGRATION PROTOCOL (19.08.2026)
     Version 1 -> 2:
       - /SystemMonitor/NetworkMonitor[@name='Ethernet']/@watchUDPPort -> renamed to @watchPort
     Version 2 -> 3.
   -->
   <SystemMonitor version="3">

Everything *between* the tags — indentation, blank lines, comments, processing instructions, entities such as ``&amp;``, whether an element was written self-closing or as an open/close pair — is preserved as it was, as are the newline style, the encoding and the byte order mark. Start tags and the XML declaration are re-emitted by the XML writer: whitespace *inside* start tags (attributes spread over several lines), single-quoted attribute values and spaces around ``=`` are normalized, self-closing tags are written as ``<x />``, and the declaration (kept if present, never added) is spelled the writer's way, e.g. ``encoding="utf-8"``. Desomnia's own rewrite of the file does not count as a change: it neither restarts the application nor triggers a reload.

.. note::

   Desomnia writes the file where the running instance found it, so the account it runs under needs write access to the file (and to the backup location); a symlinked file is updated behind the link, and the file keeps its permissions. If the write fails, ``persistent`` alone stops the application, ``transient|persistent`` logs a warning and continues in memory.

The effective configuration written by ``writeEffectiveXML`` of an ``<EnvironmentMonitor>`` configuration declares its version on the root element only: it is the result of a migration and is never migrated itself.

.. _version-2:

Version 2
---------

:Status: Current
:Since: 3.3.0

**Usage and demand events.** Periodic activity inspection now triggers ``onUsage`` on every positive inspection cycle. ``onDemand`` is reserved for external requests and is available only on capable resources, currently watched network hosts, network services, and Duo instances. Both events mark a resource as active and cancel pending ``onIdle`` actions.

The version 2 migration renames the former inspection event attributes to ``onUsage`` on ``<SystemMonitor>``, ``<ProcessMonitor>``, ``<Process>``, ``<SessionMonitor>``, session-process ``onSessionDemand``, ``<DisplayMonitor>``, ``<NetworkMonitor>``, and ``<DuoSessionMonitor>``. It deliberately preserves ``onDemand`` on network hosts, network services, and Duo ``<Instance>`` nodes because these nodes retain the external-demand event. Those nodes accept the new ``onUsage`` attribute in addition.

**Duo Stream Integration.** The :doc:`plugin </plugins/duo/plugin>` also renamed its element: ``<DuoStreamMonitor>`` became ``<DuoSessionMonitor>`` (a Duo instance is a full session, not just a stream).

Version 1
---------

:Status: Superseded by version 2 (Duo Stream Integration only)
:Since: Initial release

Version 1 is the first configuration format version. It is declared by a ``version="1"`` attribute on the root element (``<SystemMonitor version="1">``); a file without any declaration is read as version 1, too.

----

Future versions will be listed here as they are introduced, together with a description of what changed and any required migration steps.

For module and plugin authors
-----------------------------

A module states its version facts on its ``ConfigurableModule`` — ``MinVersion``, the version in which it last changed its format (raised together with the migration step for it), and ``MaxVersion``, normally left alone so the module accepts every future format (a plugin that must be strict overrides it with a literal and thereby limits the whole product) — and takes part in the migration by implementing ``IXConfigurationMigration`` (namespace ``MadWizard.Desomnia.Configuration.Xml``): ``Run(XDocument configuration, uint version)`` is called once per version step from the file's version + 1 up to and including ``MinVersion`` (``version`` being the step's target), on the raw XML document before any other processing — in the environment layout the root is ``<EnvironmentMonitor>`` and the module's elements appear once per environment block. Raising ``MinVersion`` is what *demands* the migration: a build where no loaded module raised it leaves older files alone. The version declaration itself is none of the module's business: the framework reads and stamps it. A module that raises ``MinVersion`` without implementing the interface turns every outdated file into a hard startup error.

Every change goes through the helpers of ``XMigrationExtensions`` — ``MigrateRename``, ``MigrateSetValue``, ``MigrateRemove``, ``MigrateAdd``, ``MigrateReplace``, ``MigrateMove``, or the general ``Migrate(level, reason, action)`` for anything else: each records the note first, with the node's XPath as the user's file has it, and then carries out the change (``Information`` by default, ``Warning`` for ``MigrateRemove``; an optional ``reason`` is appended to the note, a note above ``Warning`` aborts the startup, ``LogLevel.None`` records a deliberately silent change). The migration tracks the document while a step runs, so a change made past the helpers is still reported — as a ``Warning``, "outside a migration helper", which also keeps the file from being rewritten. A helper used inside another helper's action is a detail of that operation and takes no note of its own unless it is given a reason; ``AnnotateMigration(level, message)`` records a note without a change (an ``Error`` for something that cannot be migrated automatically), and a ``ConfigurationValueException`` reports a hard configuration error. ``AttributeNamed``/``ElementsNamed``/``DescendantsNamed`` look nodes up the way the configuration vocabulary works: by local name, case-insensitively.
