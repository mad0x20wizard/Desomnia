Configuration reference
=======================

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

This option determines, if sessions with no open files or folders should be considered as actual usage.

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

clientName
++++++++++

clientIP
++++++++

shareName
+++++++++

filePath
++++++++

:regex: