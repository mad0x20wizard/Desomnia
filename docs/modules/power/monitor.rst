Power Request Monitor
=====================

:OS: 🪟 *Windows*

Windows applications can register *power requests* to prevent the system from suspending while a task is in progress — a backup job, a media player, a screen recorder. The Power Request Monitor makes these requests visible to Desomnia and lets you decide which ones should count as genuine usage and which ones should be ignored.

Each ``<RequestFilterRule>`` matches requests by name and reason text (evaluated as a regular expression). Rules with ``type="MustNot"`` suppress specific requests so that they do not block sleep; rules with ``type="Must"`` allow only named requests through while ignoring everything else. As long as at least one unfiltered power request is active, the monitor reports the system as in use.

The :doc:`configuration reference <config>` lists all available attributes.

.. toctree::
   config
