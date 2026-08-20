.. image:: https://raw.githubusercontent.com/mad0x20wizard/Desomnia/main/Desomnia.png

========
Desomnia
========

|Build Status| |Build Status Docs| |Cloudsmith| |License| |Discord|

Desomnia is an intelligent sleep management solution designed for use on local networks, ranging from home labs to small businesses. It monitors the usage of your machines locally and via network to keep them awake as long as they are in use and can wake them at the exact moment you open a connection to one of it's services — transparently, with minimal or no changes at all on the connecting sides. This is achieved through a smart use of standard network protocols and a little bit of IP spoofing.

If you have ever been frustrated by the built-in sleep management features of your operating system, Desomnia can be used as a drop-in replacement. You can configure it to monitor a variety of metrics ranging from network traffic, process activitiy, native power requests / inhibitor locks, among others, that should keep the system running and Desomnia enforces exactly that — across your entire network, automatically.

Why should I need this?
-----------------------

The aim is to **reduce the energy consumption** of your self-hosted services while **maintaining availability**.

But instead of requiring you to decide when to wake and suspend the servers manually, you can declare the hosts and services that need to be available in a unified configuration format. Desomnia will then take care of providing you with an integrated and frictionless experience.

Modes of operation
------------------

Desomnia can be deployed in three complementary roles and combined across multiple machines:

1. **Local Sleep Management** – 🪟 *Windows* 🐧 *Linux*

   Replaces the OS's built-in sleep management. Desomnia holds the system awake while any monitored resource is used — a user session, a running process, an open SMB share, an incoming network connection — and sends it to sleep once everything goes quiet.

2. **Wake-on-LAN client** – 🪐 *platform-independent*

   Runs on the machine you connect *from*. When you try to open a connection to a sleeping host, Desomnia detects the attempt, sends a Magic Packet, and waits for the host to come back online — while your application notices only a small delay.

3. **Wake-on-LAN proxy** – 🪐 *platform-independent*

   Runs on an always-on device (a Raspberry Pi, a NAS, a small server). It watches the network in promiscuous mode and sends Magic Packets on behalf of any client that tries to reach a sleeping host. Nor Servers or clients need any further configuration. The best thing: it does not create bottlenecks in your infrastructure; network traffic is only rerouted and intercepted when absolutely necessary.

   - Acting as a **Sleep Proxy**, this deployment can also speak the standard multicast DNS Sleep Proxy protocol (Apple's *Bonjour Sleep Proxy* / *Wake on Demand*): instead of being configured statically, hosts **register their own services** with the proxy just before they suspend, so it keeps advertising them over mDNS and wakes the host the instant a client tries to connect. This removes the need to describe each sleeping host on the proxy by hand — and interoperates with non-Desomnia clients and proxies alike.

Monitoring
----------

In order to replace to built-in sleep management, Desomnia models your system as a tree of logical resources, which monitor and track specific types of activity. Only when all resources switch to an idle state, the system will be suspended.

The following activities can be tracked out of the box:

-  **Network traffic** – 🪐 *platform-independent*

   Tracks incoming and outgoing connections using libpcap or Npcap. You declare which hosts and which services — by TCP or UDP port — should count as activity; anything else will be ignored. For remote hosts, network demand can also trigger actions: wake a sleeping machine, start a VM, or do a Single Packet Authorization before the connection attempt is made. In *promiscuous mode*, Desomnia watches the entire broadcast domain and can act on behalf of other hosts — this is the foundation of the Wake-on-LAN deployment.

-  **Processes** – 🪐 *platform-independent*

   Watches running processes by name, with an optional CPU threshold to distinguish real activity from idle background processes. On Windows, Desomnia uses Event Trace for Windows (ETW) for near-instant start and stop notifications with no polling overhead; on other platforms it polls at a configurable interval.

-  **Power requests** – 🪟 *Windows* 🐧 *Linux*

   Tracks and filters native power requests / inhibitor locks registered by processes and drivers — giving you selective control over which of these should be allowed to prevent the system from sleep.

-  **SMB sessions** – 🪟 *Windows*

   Keeps the system awake while remote clients have open file-sharing sessions, with fine-grained filtering by username, client name, IP address, share name, or file path.

-  **User sessions** – 🪟 *Windows*

   Tracks the activity of Windows user sessions, including Remote Desktop connections. Sessions can be filtered by user account, have individual idle thresholds, and trigger actions — lock, disconnect, logout, or run a script — when they start to idle.

Configuration examples
----------------------

The following examples illustrate how you would configure the software for a selection of use cases, and should give you an idea of how Desomnia works. `Read the docs`_ to find out more about the available use-cases.

Wake-on-LAN proxy
+++++++++++++++++

Use this on an always-on device. Any client on the local network that tries to reach the server via RDP or SSH while it's suspended will trigger a Magic Packet automatically. On startup, IPv4 addresses will be resolved automatically via hostname resolution:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <SystemMonitor version="2">

     <NetworkMonitor watchMode="promiscuous" autoDetect="IPv4">
       <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E">
         <Service name="RDP" port="3389" />
         <Service name="SSH" port="22" />
       </RemoteHost>
     </NetworkMonitor>

   </SystemMonitor>

Desomnia will automatically detect and temporarily claim the sleeping hosts IP address in order to filter incoming connection attempts. After a successful wake, the connection will be handed off to the target, transparently.

Server and VM sleep automation
++++++++++++++++++++++++++++++

This configuration could be used to automatically suspend a physical system unless there is an open SSH connection or a running backup plan. Additionally Desomnia will watch connections to a local VM named ``dev``, running in bridged network mode:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <SystemMonitor version="2" timeout="2min" onIdle="sleep+20min" onDemand="sleepless">

     <NetworkMonitor>
       <Service name="SSH" port="22">

       <VirtualHost name="dev" IPv4="192.168.1.10" onDemand="start" onIdle="suspend+10min">
         <Service name="SSH" port="22" />
         <Service name="HTTP" port="80" />
       </VirtualHost>
     </NetworkMonitor>

     <PowerRequestManager>
       <RequestFilterRule type="Must" name="Backup">CBBackupPlan</RequestFilterRule>
     </PowerRequestManager>

   </SystemMonitor>

The VM starts automatically on the first SSH or HTTP connection. Any live TCP or UDP connection to one of its services — from anywhere on the network — count as activity and keeps it running. Ten minutes after the last connection closes, the VM will be suspended. The physical host will also be suspended, once neither the VM or any of its own services are needed.

Additional Features
-------------------

Thanks to its open architecture, Desomnia can be extended with plugins. A variety of optional features are already available:

-  **DuoSessionMonitor** – 🪟 *Windows*

   If you use `DuoStream <https://github.com/DuoStream>`__ to turn your computer in a multi-seat game streaming host, this plugin makes Desomnia aware of the configured streaming instances, starts them on demand when accessed by a Moonlight client and can stop them when they become idle, reducing GPU load and overall resource footprint

-  **Hyper-V support** – 🪟 *Windows*

   This plugin lets Desomnia interact with virtual machines running on the Hyper-V platform. It resolves their MAC addresses directly from the hypervisor and can start, suspend, or stop VMs in response to actual network usage — no manual lifecycle management needed.

-  **Firewall Knock Operator** – 🪐 *Platform-independent*

   Enhances Desomnia's Single Packet Authorization framework with strong cryptographic authentication, to allow you to temporarily authorise remote clients with dynamic IP addresses from the internet to wake hosts on your local network.

   🚧 This plugin aims to be fully interoperable with `fwknop <https://github.com/mrash/fwknop>`__, the established open-source SPA tool. A future version will also allow you to configure the local system firewall, providing a full drop-in replacement for the tool's original use case.

-  **FRITZ!Box Router** – 🪐 *Platform-independent*

   Integrates Desomnia with AVM FRITZ!Box routers: hosts and their MAC addresses are resolved from the box's device table (even while they sleep, when they are absent from the ARP cache), VPN clients are detected automatically, and LAN port speeds can be switched on demand. Routers are found by themselves via zero-conf discovery — a FRITZ!Box on the network is enough, no configuration required.

Observability
-------------

Desomnia uses `NLog <https://nlog-project.org/>`__ for structured logging. Out of the box it runs quietly; a wide range of log files can be created to record why the system stayed awake, which requests triggered it to send a Magic Packet and to assist with debugging problems. A usage report can be created regularily, to summarise the daily sleep and wake activity.

Getting started
---------------

🪟 Windows
+++++++++++

Download the latest release from the `GitHub releases page <https://github.com/mad0x20wizard/Desomnia/releases>`__ and run the installer. It registers the service, installs all dependencies (including Npcap), and walks you through an initial configuration. `Read the docs`_ to discover everything Desomnia can do and how to configure it.

🍺 Homebrew – 🍎 macOS / 🐧 Linux
+++++++++++++++++++++++++++++++++++

.. code:: bash

   brew install mad0x20wizard/tools/desomnia
   sudo brew services start mad0x20wizard/tools/desomnia

See the `Homebrew installation guide <https://desomnia.readthedocs.io/en/latest/installation/homebrew.html>`__ for filesystem layout, plugin installation, and platform-specific notes.

📦 Native packages – 🐧 Linux
++++++++++++++++++++++++++++++

Native ``.deb`` / ``.rpm`` packages ship the low-memory **native build** (no .NET Runtime required) and are distributed through a package repository, so Desomnia is updated together with the rest of the system. Register the repository once, then install:

.. code:: bash

   # Debian / Ubuntu / Raspberry Pi OS
   curl -1sLf 'https://dl.cloudsmith.io/public/mad0x20wizard/tools/setup.deb.sh' | sudo -E bash
   sudo apt install desomnia

   # Fedora / RHEL / openSUSE
   curl -1sLf 'https://dl.cloudsmith.io/public/mad0x20wizard/tools/setup.rpm.sh' | sudo -E bash
   sudo dnf install desomnia

The package starts Desomnia as a zero-configuration Sleep Proxy out of the box. See the `package installation guide <https://desomnia.readthedocs.io/en/latest/installation/packages.html>`__ for supported platforms and the constraints of the native build.

Package repository hosting is graciously provided by `Cloudsmith <https://cloudsmith.io/~mad0x20wizard/repos/tools/packages/>`__. Cloudsmith is the only fully hosted, cloud-native, universal package management solution, that enables your organization to create, store and share packages in any format, to any place, with total confidence.

🐋 Docker – 🐧 Linux
++++++++++++++++++++++

A Docker image is available on `Docker Hub <https://hub.docker.com/r/mad0x20wizard/desomnia>`__. A ready-to-use ``docker-compose.yml`` is provided in the repository description there. See the `Docker installation guide <https://desomnia.readthedocs.io/en/latest/installation/docker.html>`__ for volume layout and capability requirements.

Contributing
------------

- **Bug reports**: open an issue on `GitHub <https://github.com/mad0x20wizard/Desomnia/issues>`__.
- **Questions and feedback**: join the community on `Discord <https://discord.gg/RzrBjcy2>`__.
- **Feature requests**: open a GitHub issue and describe your use case — all suggestions are welcome.

System requirements
-------------------

- Windows 8 / 10 / 11, Linux, or macOS
- .NET Runtime 10, or Docker (Linux only)
- `Npcap <https://npcap.com/>`_ on Windows or `libpcap <https://github.com/the-tcpdump-group/libpcap>`_ on Linux / macOS (optional, required for NetworkMonitor)

Native build
++++++++++++

On 64-bit Linux devices you can instead use the **native build** – a single self-contained binary that needs no .NET Runtime and reduces the memory footprint by more than half (~50 MB instead of >100 MB). It is available as a `native package <https://desomnia.readthedocs.io/en/latest/installation/packages.html>`__, through the `manual installation <https://desomnia.readthedocs.io/en/latest/installation/manually.html>`__, or as a `Docker image <https://desomnia.readthedocs.io/en/latest/installation/docker.html>`__ — the guides explain when to use it and its constraints.

----

If you like this project, consider supporting it:

|"Buy Me A Coffee"|

.. |"Buy Me A Coffee"| image:: https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png
   :target: https://coff.ee/mad0x20wizard

.. |Cloudsmith| image:: https://img.shields.io/badge/OSS%20hosting%20by-cloudsmith-blue?logo=cloudsmith&style=flat-square&link=https%3A%2F%2Fcloudsmith.com
   :target: https://github.com/mad0x20wizard/Desomnia/actions/workflows/publish.yml
   :alt: Build Status

.. |Build Status| image:: https://github.com/mad0x20wizard/Desomnia/actions/workflows/publish.yml/badge.svg
   :target: https://github.com/mad0x20wizard/Desomnia/actions/workflows/publish.yml
   :alt: Build Status
.. |Build Status Docs| image:: https://readthedocs.org/projects/desomnia/badge/?version=latest
   :target: https://desomnia.readthedocs.io/
   :alt: Documentation Status
.. |License| image:: https://img.shields.io/badge/license-GPL3-blue.svg
   :alt: License

.. |Discord| image:: https://img.shields.io/badge/chat-on%20discord-7289da.svg
   :target: https://discord.gg/6x6n7pcG39
   :alt: Discord

.. _`Read the docs`: https://desomnia.readthedocs.io
