Duo Stream Integration
======================

:OS: 🪟 *Windows*

`Duo`_ runs multiple independent game-streaming instances on one Windows
machine. Each instance uses its own Remote Desktop session and embedded
`Sunshine`_ server, to which a `Moonlight`_ client connects.

Desomnia can start instances when a client requests them and stop them when
their sessions become idle. It can also keep the physical machine awake while
the monitored sessions are in use.

.. toctree::
   :maxdepth: 1

   demand
   config
   metrics
   actions

See :doc:`demand` for installation requirements, packet-capture and TCP-listener
configuration examples, and their limitations. The :ref:`duo-watch-mode` flags
let you combine registry, event-log, and polling sources for instance state
with either packet capture or TCP listeners for network demand.

Running an instance is not by itself a reason to stay awake: input, streaming,
and any configured application activity determine whether it is in use. The
:doc:`configuration reference <config>` explains how to customize that policy.

.. _`Duo`: https://github.com/DuoStream/Duo
.. _`Sunshine`: https://github.com/LizardByte/Sunshine
.. _`Moonlight`: https://moonlight-stream.org
