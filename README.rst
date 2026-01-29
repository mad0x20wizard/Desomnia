========
Desomnia
========

|Build Status| |Build Status Docs| |License|

This background service monitors various system resources and takes action when they become idle or are accessed on demand. It can replace your OS's built-in sleep management to make the behaviour of when your system stays awake or suspends more predictable. It includes a sophisticated network monitor that allows arbitrary hosts or services to be started automatically when they are accessed.

Why should I need this?
-----------------------

Desomnia is for lazy people, who operate any amount of energy-hungry headless computer systems in their home lab and give some thought about the resulting energy consumption (but don't want to be bothered, thinking about it too much).

Some may have found, that the OS's built-in system for power requests can sometimes be too inflexible and error prone. You may already have built a variety of scripts and switches to wake up remote machines when you need them. Desomnia aims to solve this problem completely by giving you declarative control over the power states of the hosts in your network.

Modes of operation
------------------

There are three practical scenarios in which Desomnia can be deployed, to support you on your quest to optimize the energy consumption of your network:

1. **Local Resource Monitor**: You have a powerful system, that should stay awake when selected services and resources are in use. Desomnia will replace the built-in sleep management and prevent suspension as long as resources are in use, starting and stopping individual resources approriately, until the system as a whole is considered idle and sent to sleep. [🪟 *Windows-only – for now*]

2. **Automatic Wake-on-LAN client**: You have any number of servers in your network that should wake up the moment you try to establish a connection to them. Desomnia will monitor your outgoing network traffic in order to determine when to send a Magic Packet to their respective MAC address. This works with any IP service, but it can also be narrowed down to a single TCP or UDP service.

3. **Automatic Wake-on-LAN proxy**: This is basically the same as use-case 2, but you will run Desomnia on an always-on device (like a Raspberry PI) to monitor the traffic of the whole broadcast domain. When it detects, that a client wants to establish a connection to a service of a sleeping host, it will send a Magic Packet on their behalf. This allows any client to perform Wake-on-LAN transparently and without modification. This does NOT create a single point of failure. Network traffic is only intercepted when absolutely necessary.

You can either combine options 1 and 2, or use them separately. However, due to the always-on requirement, option 3 cannot be used alongside the others – at least on a single computer. It can actually be beneficial to use all three deployments, but each on a different computer.

Core Modules
------------

Features marked with a construction sign (🚧) are not fully operational yet, but will be released with a future version. Some features were primarily developed for Windows (🪟), while others will also work on Linux or macOS (🐧, 🍎). Support for the core features is planned on all three platforms, provided there is an actual demand for it.

NetworkMonitor
~~~~~~~~~~~~~~

:OS: 🪟 *Windows* 🐧 *Linux* 🍎 *macOS*

Utilizes the free `npcap <https://npcap.com/>`__ or libpcap library to monitor network traffic, in order to determine if the system is idle.

- IPv4 and IPv6 support.
- Configure IP addresses statically of let them automatically be discovered.
- Define arbitrary network services (by TCP or UDP port) to filter the watched packets.
- Configure triggers to take actions when a service is accessed or starts to idle.

There are three types of hosts, that can be monitored:

1. **Local host**: Incoming traffic is monitored, which keeps the system from sleeping.
2. **Remote hosts**: Outgoing traffic is monitored, which also keeps the system from sleeping. Additionally, if accessed, you can also wake them with a Magic Packets or announce your connection attempt with a SPA.
3. **Virtual hosts**: These behave like remote hosts, but can also be stopped if idle. Specific virtual machine (VM) providers need to be enabled via plug-ins.

SessionMonitor
~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

In Windows, each logged-in user is represented as a session. Several sessions can exist simultaneously. Typically, an active user session would prevent the system from suspending. This module allows you to decide whether this fits your design.

- Takes the standard ``LastInputTime`` value of the user session into account.
- Connected Remote Desktop connections (RDP) are always considered as active.
- Designate a CPU threshold for process groups within a session that will also count as user activity.
- Configure actions to be taken when a user logs in or a session becomes idle (``lock``, ``logout``, ``disconnect``, ``execute`` a program/script, ...)

NetworkSessionMonitor
~~~~~~~~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

In Windows, a connection to shared files and folders (SMB) is referred to as a "network session". Accessing a single network resource usually prevents the system from suspending. This module allows you to specify rules to filter exactly which resources are included or excluded:

- Filter by share name
- Filter by remote username
- Filter by remote client name
- Filter by remote IP
- Filter by file path

PowerRequestMonitor
~~~~~~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

In Windows, individual processes can register "power requests" that prevent the system from going to sleep. Sometimes, you may encounter a request that you regard as unwarranted. This module enables you to take native power requests into account while allowing you to include or exclude individual requests.

ProcessMonitor
~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

Many programs can be configured to request that the system stays awake. However, if a program lacks this feature, you can configure individual processes or groups of processes by name to prevent suspension while running. You can also designate a CPU threshold that must be reached to filter out unimportant activity.

Additional Features
-------------------

Thanks to its open architecture, Desomnia's functionality can be expanded using plug-ins. A variety of optional features are already available in this way:

Firewall Knock Operator
~~~~~~~~~~~~~~~~~~~~~~~

:OS: 🪐 *Platform-independent*

If you receive requests from outside your local network (for example, via port forwarding through your router), you can configure Desomnia to allow or block specific network masks statically. This ensures that hosts on your network won't wake up unnecessarily.

However, if you are using a mobile device with a dynamic IP address, you may want to temporarily allow access from specific IP addresses. This plugin uses the FKO protocol to send and receive SPA (Single Packet Authorization) messages in order to configure the embedded packet filter, automatically.

🚧 Future releases will also enable configuration of the local system firewall, which was `fwknop <https://github.com/mrash/fwknop>`__'s intended use case. This will offer a full-fledged replacement for this useful tool, which seems to have been abandoned.

HyperV support
~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

This plugin allows Desomnia to interact with virtual machines running on the Hyper-V platform. It gives you the ability to monitor usage of their respective netwrok services (to determine the actual usage of the host system) and start or stop them on demand.

🚧 Interactive Taskbar Icon
~~~~~~~~~~~~~~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

Incarnates a little helper process in each user session, that communicates with the background service to display information and to allow manual control of the sleep cycle:

- Configure the system to refrain from sleep, either indefinitely or for a set amount of time.
- Temporarily return sleep management to the OS.
- Monitor the reasons why the system is staying awake in real time.

🖥️ As an additional feature, you will be able to enable certain users to instantly switch console sessions without authentication. This allows you to use multiple user sessions, similar to having multiple desktops, but with strong isolation.

DuoStreamMonitor
~~~~~~~~~~~~~~~~

:OS: 🪟 *Windows-only*

For those who are enthusiastic users of `DuoStream <https://github.com/DuoStream>`__, this plugin makes Desomnia aware of the configured instances:

- Start instances on demand, when they are accessed by a Moonlight client (no further clientside configuration needed).
- Stop instances after they become idle, to reduce power consumption of the GPU and to reduce the overall footprint of system resources.
- The system will stay awake until the last session disconnects.

etc.
~~~~

If you find, that a cruscial feature is missing, don't hesitate to open an issue and explain why Desomnia should have support for your use case. Alternatively if you are adept at programming in C#, you can check out the provided 🚧 **example project** and develop your own extension plugin, to make Desomnia aware of your needs.

Observability
-------------

You can configure Desomnia to create a log of the reasons why the local system was prevented from sleeping or why a remote system was woken up recently. In addition, several logs can be activated to analyse why a particular use case did not work.

How to get started
------------------

🪟 Windows
~~~~~~~~~~

The easiest way to setup Desomnia on Windows is to use it's installer, which allows you to set everything up and running in a minute.

It does the work for you, to register Desomnia as a system service, download and install all necessary dependencies and guide you through a basic configuration of the parameters. Nevertheless, you are encouraged to 🚧 `Read the Docs <https://desomnia.readthedocs.io/>`__ to discover, what Desomnia can do for you and how to configure it.

If it happens that you don't like Desomnia, the uninstaller will help you to remove everything from your system completely. For your convenience, you can run the installer again (or hit "Modify" in the system settings) to add/remove some of the optional features later on.

🪄 To begin just download the latest release from GitHub and follow the steps of the 🧙 Wizard.

🐧 Linux
~~~~~~~~~

The easiest way to get an instance of Desomnia running is inside a 🐋 `Docker container <https://hub.docker.com/r/mad0x20wizard/desomnia>`__.

🚧 Futher information will be provided to set everything up with ``docker compose``.

System Requirements
-------------------

- Windows 8 / 10 / 11 or Linux or macOS
- .NET Runtime 9 / 10
- `npcap <https://npcap.com/>`__ on Windows / libpcap (optional, needed for NetworkMonitor)

I you like my work, consider to:

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
