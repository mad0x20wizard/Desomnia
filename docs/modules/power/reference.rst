Configuration reference
=======================

PowerRequestMonitor
-------------------

You can configure any number of ``<RequestFilterRule>`` to describe which power requests should be considered as usage.

.. code:: xml

  <SystemMonitor>

    <PowerRequestMonitor>

      <RequestFilterRule ... />
      <RequestFilterRule ... />

    </PowerRequestMonitor>

  </SystemMonitor>

RequestFilterRule
-----------------

.. code:: xml

  <RequestFilterRule type="MustNot" name="Backup">CBBackupPlan</RequestFilterRule>

.. include:: /attributes/filtertype.rst

name
++++

You can provide any logical name here, to describe the power request or group of power requests. This name will be used to represent these requests in the log.

text
++++

:🔍 regex:

The text node of the ``<RequestFilterRule>`` will be parsed as a regular expression and matched against the name and reasons of the power request.