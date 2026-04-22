Duo Stream Integration
======================

:OS: 🪟 *Windows*

`Duo`_ is an HDR-compatible multiseat game streaming solution for Windows. It builds on top of `Sunshine`_ to overcome one of its core limitations: Sunshine in its basic form supports only a single streaming session, tied to the Windows Console session. Duo enables multiple users to stream simultaneously and independently from the same machine by running each session inside a dedicated Remote Desktop (RDP) session, linked to a specific Windows user account.

Each Duo **instance** is a logical streaming endpoint with a unique base port. When an instance is started, Duo creates an RDP session for its associated user and launches an embedded Sunshine server inside it. `Moonlight`_ clients then connect to it exactly as they would to a standalone Sunshine server. Duo adds further enhancements on top — for example, automatically adjusting the virtual display resolution to match the connecting client.

By default, instances can only be started and stopped manually through the Duo Manager and it's web interface, or configured to start automatically when the Duo service starts. This plugin lets Desomnia automate the instance lifecycle: instances are started on demand when a Moonlight client connects, and stopped once no client has been active for the configured timeout. The physical system is kept awake for as long as at least one instance is running. See :doc:`demand` to learn about the options you can choose from.

Since most configuration is optional, a minimal setup looks like this:

.. code:: xml

  <SystemMonitor timeout="5min">
    <NetworkMonitor ... /> <!-- optional, needs Npcap installed -->

    <DuoStreamMonitor onInstanceDemand="start" onInstanceIdle="stop" />
  </SystemMonitor>

.. toctree::
    :maxdepth: 2

    demand
    actions
    config

.. _`Duo`: https://github.com/DuoStream/Duo
.. _`Sunshine`: https://github.com/LizardByte/Sunshine
.. _`Moonlight`: https://moonlight-stream.org
