Hyper-V Support
===============

:OS: 🪟 *Windows*

The Hyper-V plugin provides the implementations for the ``start``, ``suspend``, and ``stop`` actions on a ``<LocalHost>`` × ``<VirtualHost>`` configuration described in :doc:`/modules/network/virtual`. When those actions are triggered by the ``onDemand`` and ``onIdle`` events, the plugin carries them out via the Hyper-V API. The physical host is kept awake for as long as any of the VM's services are in use.

Beyond the lifecycle management that any hypervisor plugin provides, the Hyper-V plugin can query the MAC addresses of virtual machines directly from the hypervisor. This means a ``<VirtualHost>`` does not require a hardcoded ``MAC`` attribute — Desomnia resolves it automatically at startup.

.. attention::

   While MAC addresses are resolved automatically, IP addresses are not: a VM that is switched off has no address to report. IP address resolution still requires an external authority such as a router with DNS support and :doc:`auto-configuration </modules/network/auto>` enabled.

To use this plugin, the `Hyper-V platform`_ must be enabled on your system.

Network interface selection
---------------------------

Hyper-V in bridged mode creates a virtual network switch connected to a specific physical adapter. The NetworkMonitor should be bound to the virtual interface, since that is the one carrying the IP configuration — this happens automatically when you :doc:`select the interface by network or by gateway presence </modules/network/interface>`.

At startup, the plugin detects that a virtual adapter is selected and redirects packet capture to the underlying physical adapter. This is necessary to observe all traffic destined for virtual machines, not only packets addressed to the physical host itself.

Example configuration
---------------------

The following configuration keeps a self-hosted GitLab VM running only when it is actually in use. The VM starts automatically when a client connects to one of its services and suspends once it has been idle for ten minutes:

.. code:: xml

  <SystemMonitor version="1" timeout="2min" onIdle="sleep+1h" onDemand="sleepless">

    <NetworkMonitor network="192.168.178.0/24" autoDetect="MAC|IPv4|IPv6" watchYield="true">

      <Service name="SSH" port="22" />

      <VirtualHost name="gitlab" onIdle="suspend+10min">
        <Service name="SSH" port="22" />
        <Service name="HTTP" port="80" />
      </VirtualHost>

    </NetworkMonitor>

  </SystemMonitor>

The outer ``<Service>`` elements on ``<NetworkMonitor>`` cover services of the physical host itself, keeping it awake when accessed directly. The VM's own ``<Service>`` declarations track its services independently.

The network activity is checked every two minutes (the global ``timeout``). If no traffic to any of the VM's services is detected during a check, the ten-minute ``onIdle`` delay begins. Traffic arriving during that delay resets the timer and the VM stays running. Once the delay expires without further activity, the VM is suspended.

The physical host follows the same logic at the outer level: it will only suspend if neither the VM nor its own services have seen activity for a full hour.

Yielding
--------

The attribute ``watchYield="true"`` instructs Desomnia to broadcast a suspension announcement before the physical host goes to sleep, so that another instance running in :doc:`promiscuous mode </modules/network/promiscuous>` on the network can immediately take over responsibility for the configured hosts. See :doc:`/modules/network/yield` for details.

.. _`Hyper-V platform`: https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/install-hyper-v
