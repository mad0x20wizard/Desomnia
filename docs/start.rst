Getting started
===============

Desomnia runs as a background service with elevated privileges, so that it can monitor system activity and control sleep behaviour. Before installing, pick the method that fits your platform and preferences.

Available installation methods
------------------------------

.. list-table::
   :header-rows: 1
   :widths: 20 15 65

   * - Method
     - Platform
     - Notes
   * - :doc:`Windows installer </installation/installer>`
     - 🪟 Windows
     - Installs all dependencies, registers the service, and creates an initial configuration.
   * - :doc:`Homebrew </installation/homebrew>`
     - 🐧 Linux 🍎 macOS
     - Manages dependencies and service registration automatically.
   * - :doc:`Docker </installation/docker>`
     - 🐧 Linux
     - Containerised deployment. All dependencies are bundled; no installation required.
   * - :doc:`Manually </installation/manually>`
     - 🐧 Linux
     - Requires you to install the .NET runtime and libpcap yourself.

What to read next
-----------------

Once Desomnia is installed and running, the :doc:`/guides/sleep` guide is the best place to start, if you want to **replace the built-in power management** with Desomnia's configurable monitoring. Local resource management is currently supported on **Windows only**; on Linux and macOS, the primary use cases are as a :doc:`WoL client </guides/wol-client>` or :doc:`network proxy </guides/wol-proxy>`.

If your primary goal is to **wake remote hosts on demand**, head directly to the Wake-on-LAN guides: start with :doc:`/guides/wol-client` if Desomnia should run locally on the machine that initiates the connection, or :doc:`/guides/wol-proxy` if you want it to run on an always-on device that watches the network and send Magic Packets on behalf of other hosts. The proxy deployment is the most sophisticated and feature rich.

If you want to reach sleeping hosts from **outside your local network**, read the :doc:`/guides/remote-access` guide. It walks through four approaches in order of complexity: plain port forwarding, routed unicast Magic Packets (no always-on proxy required if your router supports static ARP entries), VPN-backed proxy mode, and Single Packet Authorization (SPA) for authenticated, on-demand access without a persistent tunnel.

If anything does not behave as expected, consult the :doc:`troubleshooting </modules/network/troubleshooting>` page. Enabling :doc:`logging </concepts/logging>` is usually the first step — Desomnia's output is minimal by default and a log file will reveal what it is detecting and which actions it is firing.
