Configuration
=============

NetworkSessionMonitor
---------------------

You can configure any number of ``<FilterRules>`` to describe which network sessions should be considered as usage.

.. code:: xml

  <SystemMonitor>

    <NetworkSessionMonitor watchPassive="true">

      <FilterRule ... />
      <FilterRule ... />

    </NetworkSessionMonitor>

  </SystemMonitor>

watchPassive
++++++++++++

:default: ``true``

This option determines, if sessions with no open files or folders should also be considered as usage.

FilterRule
----------

.. code:: xml

  <FilterRule type="MustNot"
    userName="test"
    clientName="DESKTOP-123456" 
    clientIP="192.168.178.20"
    
    shareName="music"
    filePath="albums/xyz"
  />

.. include:: /attributes/filtertype.rst

userName
++++++++

The Windows account name of the connected user. Matched case-insensitively.

clientName
++++++++++

The NetBIOS or DNS host name of the connecting client machine. Matched case-insensitively.

clientIP
++++++++

The IP address of the connecting client. Must be an exact address — CIDR ranges are not supported.

shareName
+++++++++

The name of the SMB share being accessed. Matched case-insensitively. This criterion is only evaluated for sessions that have open files; it has no effect on passive sessions.

filePath
++++++++

:🔍 regex:

A regular expression matched against the **absolute** path of each open file within the session, relative to the filesystem root. This criterion is only evaluated for sessions that have open files; it has no effect on passive sessions.
