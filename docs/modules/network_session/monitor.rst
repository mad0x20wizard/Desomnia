Network Session Monitor
=======================

:OS: 🪟 *Windows*

The Network Session Monitor tracks active SMB file-sharing sessions and treats them as system usage. As long as a remote client has an open session — browsing a share, reading or writing files — the system is considered in use and will not be allowed to sleep.

By default, sessions with no open files or folders (``watchPassive="true"``) are also counted, covering clients that have authenticated but are not actively accessing files. Each ``<FilterRule>`` can match on user name, client host name, client IP address, share name, or file path (the latter as a regular expression), so it is straightforward to ignore background connections from backup agents or monitoring tools while keeping user-initiated sessions active.

The :doc:`configuration reference <config>` lists all available attributes.

.. toctree::
   config
