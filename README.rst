========
Desomnia
========

|Build Status| |Build Status Docs| |License| |Discord|

**Desomnia** is an intelligent power manager for home labs and small businesses. It keeps your machines asleep until they are actually needed, wakes them the moment a connection arrives, and puts them back to sleep once the last client disconnects — transparently, with minimal or no changes at all on the connecting sides.

The built-in sleep management in your OS was designed for desktops. It has little awareness of the network, cannot tell the difference between a process that matters and background noise, and has no mechanism to restart a host after it suspended. Desomnia replaces it with a declarative, network-aware monitoring system: you describe the services, sessions, and processes that should keep your machines awake, and Desomnia enforces exactly that — across your entire network, automatically.

Why should I need this?
-----------------------

Desomnia is for people who operate energy-hungry headless servers in their home lab and actually care about the electricity bill — but do not want to think about it constantly.

Maybe you already have a tangle of scripts to wake machines when you need them. Maybe your file server stays on all night because Windows decided a background process counts as "user activity". Maybe you have wanted to put your Hyper-V host to sleep between uses but could never find a clean way to bring it back up. Desomnia solves all of this with a single declarative configuration format.

Modes of operation
------------------

Desomnia can be deployed in three complementary roles and combined across multiple machines:

1. **Local Sleep Management** – 🪟 *Windows*

   Replaces the OS's built-in sleep management. Desomnia holds the system awake while any watched resource is active — a user session, a running process, an open SMB share, an incoming network connection — and sends it to sleep once everything goes quiet.

2. **Wake-on-LAN client** – 🪐 *platform-independent*

   Runs on the machine you connect *from*. When you try to open a connection to a sleeping host, Desomnia detects the attempt, sends a Magic Packet, and waits for the host to come back online — while your application notices only a small delay.

3. **Wake-on-LAN proxy** – 🪐 *platform-independent*
   
   Runs on an always-on device (a Raspberry Pi, a NAS, a small server). It watches the network in promiscuous mode and sends Magic Packets on behalf of any client that tries to reach a sleeping host. Nor Servers or clients need any further configuration. The best thing: it does not create bottlenecks in your infrastructure; network traffic is only rerouted and intercepted when absolutely necessary.

Monitoring
----------

Desomnia models your system as a tree of logical resources. The root is the ``<SystemMonitor>`` — representing the machine as a whole — and below it, monitors each track a specific type of activity. A monitor is active if any resource it watches is in use; the system goes to sleep only when every monitor reports idle simultaneously.

The following activities can be tracked out of the box:

-  **Network activity** – 🪐 *platform-independent*

   Tracks incoming and outgoing connections using libpcap or Npcap. You declare which hosts and which services — by TCP or UDP port — should count as activity; anything else is invisible to Desomnia. For remote hosts, network demand can also trigger actions: wake a sleeping machine, start a VM, or issue a Single Packet Authorization knock before the connection attempt is made. In *promiscuous mode*, Desomnia watches the entire broadcast domain and can act on behalf of other hosts — this is the foundation of the Wake-on-LAN deployment.

-  **User sessions** – 🪟 *Windows*

   Tracks the activity of Windows user sessions, including Remote Desktop connections. Sessions can be filtered by user account, have individual idle thresholds, and trigger actions — lock, disconnect, logout, or run a script — when they go quiet.

-  **Processes** – 🪐 *platform-independent*

   Watches running processes by name, with an optional CPU threshold to distinguish real activity from idle background processes. On Windows, Desomnia uses Event Trace for Windows (ETW) for near-instant start and stop notifications with no polling overhead; on other platforms it polls at a configurable interval.

-  **SMB sessions** – 🪟 *Windows*

   Keeps the system awake while remote clients have open file-sharing sessions, with fine-grained filtering by username, client name, IP address, share name, or file path.

-  **Power requests** – 🪟 *Windows*

   Tracks and filters the power requests registered by processes and drivers — giving you selective control over something the OS's own override mechanism cannot reliably provide.

Configuration examples
----------------------

Wake-on-LAN proxy
+++++++++++++++++

Place this on an always-on device. Any client on the network that tries to reach the workstation via RDP or SSH while it's offline will trigger a Magic Packet automatically:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor watchMode="promiscuous" autoDetect="IPv4">
       <RemoteHost name="workstation" MAC="00:1A:2B:3C:4D:5E">
         <Service name="RDP" port="3389" />
         <Service name="SSH" port="22" />
       </RemoteHost>
     </NetworkMonitor>

   </SystemMonitor>

Desomnia will automatically detect and temporarily claim the sleeping hosts IP address in order to filter incoming connection attempts. After a successful wake, the connection will be handed off to the target, transparently.

VM state automation
+++++++++++++++++++

This configuration keeps a virtual machine suspended to disk and starts it the moment its services are accessed:

.. code:: xml

   <?xml version="1.0" encoding="utf-8"?>
   <SystemMonitor version="1" timeout="2min" onIdle="sleep" onDemand="sleepless">

     <NetworkMonitor>
       <VirtualHost name="dev-vm" IPv4="192.168.1.10" onDemand="start" onIdle="suspend+10min">
         <Service name="SSH" port="22" />
         <Service name="HTTP" port="80" />
       </VirtualHost>
     </NetworkMonitor>

   </SystemMonitor>

The VM starts automatically on the first SSH or HTTP connection. Any live TCP or UDP connection to one of its services — from anywhere on the network — counts as activity and keeps it running. Ten minutes after the last connection closes, the VM suspends. The physical host follows the same logic and goes to sleep once the VM is no longer needed.

Additional Features
-------------------

Thanks to its open architecture, Desomnia can be extended with plugins. A variety of optional features are already available:

DuoStreamMonitor
++++++++++++++++

:OS: 🪟 *Windows*

For enthusiastic users of `DuoStream <https://github.com/DuoStream>`__, this plugin makes Desomnia aware of configured streaming instances:

- Start instances on demand when accessed by a Moonlight client — no client-side configuration required.
- Stop instances after they become idle, reducing GPU load and overall resource footprint.
- The physical system stays awake until the last streaming session disconnects.

Hyper-V support
++++++++++++++++

:OS: 🪟 *Windows*

This plugin lets Desomnia interact with virtual machines running on the Hyper-V platform. It resolves VM MAC addresses directly from the hypervisor and can start, suspend, or stop VMs in response to ``onDemand`` and ``onIdle`` events — no manual lifecycle management anymore.

Firewall Knock Operator
++++++++++++++++++++++++

:OS: 🪐 *Platform-independent*

The FKO plugin extends Desomnia's built-in Single Packet Authorization with cryptographically strong authentication. Instead of a cleartext shared secret, the client sends a single short UDP packet encrypted with AES (128, 192, or 256-bit) and authenticated with HMAC. Desomnia validates the packet and temporarily authorizes the sender's IP address; any connection attempt from that IP during the authorization window wakes the target host — no persistent tunnel required.

This makes it the ideal solution for accessing home lab services from a mobile device or any location with a dynamic IP address: knock once, the host wakes up, and you connect directly — with none of the latency overhead of a VPN.

The plugin is fully interoperable with `fwknop <https://github.com/mrash/fwknop>`__, the established open-source SPA tool. Optional replay protection binds each knock to a timestamp and the sender's IP address, preventing recorded packets from being replayed.

🚧 A future version will also configure the local system firewall, providing a full drop-in replacement for fwknop's original use case.

Observability
-------------

Desomnia uses `NLog <https://nlog-project.org/>`__ for structured logging. Out of the box it runs quietly; a log file can be enabled with a short configuration to record why the system stayed awake, which resources triggered a wake-up, and what actions were executed. A built-in usage report archives daily summaries of sleep and wake activity.

Getting started
---------------

🪟 Windows
+++++++++++

Download the latest release from the `GitHub releases page <https://github.com/mad0x20wizard/Desomnia/releases>`__ and run the installer. It registers the service, installs all dependencies (including Npcap), and walks you through an initial configuration. `Read the docs <https://desomnia.readthedocs.io/>`__ to discover everything Desomnia can do and how to configure it.

🍎 macOS / 🐧 Linux (Homebrew)
+++++++++++++++++++++++++++++++

.. code:: bash

   brew install mad0x20wizard/tools/desomnia
   sudo brew services start mad0x20wizard/tools/desomnia

See the `Homebrew installation guide <https://desomnia.readthedocs.io/en/latest/installation/homebrew.html>`__ for filesystem layout, plugin installation, and platform-specific notes.

🐋 Linux (Docker)
++++++++++++++++++

A Docker image is available on `Docker Hub <https://hub.docker.com/r/mad0x20wizard/desomnia>`__. A ready-to-use ``docker-compose.yml`` is provided in the repository description there. See the `Docker installation guide <https://desomnia.readthedocs.io/en/latest/installation/docker.html>`__ for volume layout and capability requirements.

Contributing
------------

- **Bug reports**: open an issue on `GitHub <https://github.com/mad0x20wizard/Desomnia/issues>`__.
- **Questions and feedback**: join the community on `Discord <https://discord.gg/RzrBjcy2>`__.
- **Feature requests**: open a GitHub issue and describe your use case — all suggestions are welcome.

System requirements
-------------------

- Windows 8 / 10 / 11, Linux, or macOS
- .NET Runtime 9 / 10, or Docker (Linux only)
- `Npcap <https://npcap.com/>`_ on Windows or `libpcap <https://github.com/the-tcpdump-group/libpcap>`_ on Linux / macOS (optional, required for NetworkMonitor)

----

If you like this project, consider supporting it:

|"Buy Me A Coffee"|

.. |"Buy Me A Coffee"| image:: https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png
   :target: https://coff.ee/mad0x20wizard

.. |Build Status| image:: https://github.com/mad0x20wizard/Desomnia/actions/workflows/publish.yml/badge.svg
   :target: https://github.com/mad0x20wizard/Desomnia/actions/workflows/publish.yml
   :alt: Build Status
.. |Build Status Docs| image:: https://readthedocs.org/projects/desomnia/badge/?version=latest
   :target: https://desomnia.readthedocs.io/
   :alt: Documentation Status
.. |License| image:: https://img.shields.io/badge/license-GPL3-blue.svg
   :alt: License

.. TODO: Create Discord server

.. |Discord| image:: https://img.shields.io/badge/chat-on%20discord-7289da.svg
   :target: https://discord.gg/RzrBjcy2
   :alt: Discord
