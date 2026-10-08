Getting started
===============

Desomnia needs to run as a background service with elevated privileges, so that it can monitor system activity and control sleep behaviour.

Available installation methods
------------------------------

To install Desomnia, pick the method that fits your platform and preferences:

:doc:`Interactive installer </installation/installer>` – 🪟 Windows
  | Installs required dependencies, registers the service, and creates an initial configuration.
:doc:`Homebrew </installation/homebrew>` – 🐧 Linux, 🍎 macOS
  | Direct installation, with the features supported by your operating system. Installs required dependencies, helps with service registration.
  | Update to newer versions easily.
:doc:`Native packages </installation/packages>` – 🐧 Linux
  | Native ``.deb`` / ``.rpm`` deployment for Debian, Ubuntu, Raspberry Pi OS, Fedora and openSUSE, with all features available except runtime plugin loading. Bundles the low-memory native build; no .NET runtime required.
  | Registers the service and ships a ready-to-run default configuration — a good fit for headless servers and single-board computers.
  | Update to newer versions easily via the package repository.
:doc:`Docker </installation/docker>` – 🐧 Linux
  | Containerised deployment, with limited features available. Required dependencies are bundled; no installation required. 
  | Update to newer versions easily.
:doc:`Manually </installation/manually>` – 🐧 Linux
  | Requires you to install the .NET runtime and additional libraries/tools yourself. Can be used to test Desomnia in portable mode.

What to read next
-----------------

Once Desomnia is installed, you have to choose how you want to operate Desomnia primarily on that system, since there are individual guides written for each style. However, since Desomnia has a unified codebase, these roles can also be used together.

Local Sleep Management
++++++++++++++++++++++

:OS: 🪟 *Windows* 🐧 *Linux* 🍎 *macOS*

If you want to **replace the built-in power management** with Desomnias configurable monitoring, the :doc:`/guides/sleep` guide is the best place to start. You can use the automatic Wake-on-LAN feature here as well.

Automatic Wake-on-LAN
+++++++++++++++++++++

:OS: 🪐 *Platform-independent*

If your primary goal is to **wake remote hosts on demand**, head directly to the Wake-on-LAN guides: start with :doc:`/guides/wol-client` if Desomnia should run locally on the machine that initiates the connection, or :doc:`/guides/wol-proxy` if you want it to run on an always-on device that watches the network and send Magic Packets on behalf of other hosts. The proxy deployment is the most sophisticated and feature rich.

Remote Access
~~~~~~~~~~~~~

If you want to reach sleeping hosts from **outside your local network**, read the :doc:`/guides/remote-access` guide. It walks through four approaches in order of complexity: plain port forwarding, routed unicast Magic Packets (no always-on proxy required if your router supports static ARP entries), VPN-backed proxy mode, and Single Packet Authorization (SPA) for authenticated, on-demand access without a persistent tunnel.

.. note::

  If anything does not behave as expected, consult :doc:`/guides/troubleshooting` first, then :doc:`network troubleshooting </modules/network/troubleshooting>` for connection problems. Enabling :doc:`logging </concepts/logging>` is usually the first step — Desomnias output is minimal by default and a log file will usually reveal what's going on inside.

Configuration topics
--------------------

Use :doc:`/concepts/metrics` to choose which workloads keep the machine awake,
:doc:`/modules/display/monitor` for display and lid rules, and
:doc:`/concepts/environments` to change settings automatically on different
networks or power sources. When upgrading an existing installation, start with
:doc:`/concepts/version`.
