Getting started
===============

Desomnia runs as a background service with elevated privileges, so that it can monitor system activity and control sleep behaviour. Before installing, pick the method that fits your platform and preferences.

Choosing an installation method
--------------------------------

.. list-table::
   :header-rows: 1
   :widths: 20 15 65

   * - Method
     - Platform
     - When to use
   * - :doc:`Windows installer </installation/installer>`
     - 🪟 Windows
     - Recommended for Windows. Handles all dependencies, registers the service, and walks you through an initial configuration.
   * - :doc:`Homebrew </installation/homebrew>`
     - 🐧 Linux 🍎 macOS
     - Recommended for Linux and macOS. Manages dependencies and service registration automatically.
   * - :doc:`Docker </installation/docker>`
     - 🐧 Linux
     - Good if you prefer containerised deployments. All dependencies are bundled; no separate installation step required.
   * - :doc:`Manually </installation/manually>`
     - 🐧 Linux
     - Full control over binary placement and service configuration. Requires you to install the .NET runtime and libpcap yourself.

What to read next
-----------------

Once Desomnia is installed and running, the :doc:`/guides/sleep` guide is the best place to start, if you want to replace the built-in power management with Desomnia's configurable monitoring.

If your primary goal is to wake remote hosts on demand, head directly to the Wake-on-LAN guides: start with :doc:`/guides/wol-client` if Desomnia should run locally on the machine that initiates the connection, or :doc:`/guides/wol-proxy` if you want it to run on an always-on device that watches the network and send Magic Packets on behalf of other hosts. The proxy deployment is the most sophisticated and feature rich.

If you want to reach sleeping hosts from **outside your local network** — without keeping a persistent VPN tunnel open — read the :doc:`/guides/remote-access` guide. It builds on the proxy setup and explains how to use Single Packet Authorization (SPA) to let Desomnia wake hosts in response to authenticated requests arriving from the internet, while remaining completely silent to port scanners and automated bots. This is relevant when you have a router that forwards ports into your network and you want to trigger wake-ups from a laptop on the road or a mobile connection.
