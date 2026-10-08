Configuration
=============

PowerRequestMonitor
-------------------

You can configure any number of ``<RequestFilterRule>`` to describe which power requests should be considered as usage.

The following sketch shows where filter rules go; ``...`` marks omitted settings.
The ``watchOperation`` and ``watchMode`` attributes only apply on Linux.

.. code:: xml
  :force:

  <SystemMonitor>

    <PowerRequestMonitor watchOperation="sleep" watchMode="block|block-weak">

      <RequestFilterRule ... />
      <RequestFilterRule ... />

    </PowerRequestMonitor>

  </SystemMonitor>

watchOperation
++++++++++++++

:default: ``sleep``
:OS: 🐧 *Linux*

Controls which inhibition operations are visible to the ``<PowerRequestMonitor>``. Only locks whose operation matches one of the configured values are evaluated by filter rules and counted as usage. Multiple values can be combined with ``|``.

.. include:: inhibition/operation.rst

watchMode
+++++++++

:default: ``block|block-weak``

:OS: 🐧 *Linux*

Controls which inhibition modes are visible to the ``<PowerRequestMonitor>``. Only locks whose mode matches one of the configured values are evaluated. Multiple values can be combined with ``|``.

.. include:: inhibition/mode.rst

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
