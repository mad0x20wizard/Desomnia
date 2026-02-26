Duo Stream Integration
======================

:OS: 🪟 *Windows-only*

If you use `Duo`_ to play your games remotely from a network client, this plugin enables you to automate the lifecycle of Duo instances. Normally you can only decide to start them automatically on Windows startup or start and stop them manually over a web interface. Although an idle Duo instance does not consume huge amounts of resources, shutting them down when they are not required can still make a noticeable difference when using the computer for other tasks over prolonged periods.

Since most of its configuration optional, you typically only need to add the following to your ``<SystemMonitor>`` in order to start and stop Duo instances automatically:

.. code:: xml

  <SystemMonitor timeout="5min">

    <DuoStreamMonitor onInstanceDemand="start" onInstanceIdle="stop" />

  </SystemMonitor>

.. toctree::
    :maxdepth: 2

    actions
    config

.. _`Duo`: https://github.com/DuoStream/Duo