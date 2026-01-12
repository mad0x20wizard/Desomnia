# Desomnia

Your system service for fine-grained monitoring of resource usage and sleep control. Features event-based action triggers and provides an extensible framework for customization. 

## Why should I need this?

Desomnia is for those who find, that Windows' built-in method for power requests is too inflexible and error prone. The main target group are nerds, who operate a Windows system as a home lab or some other kind of headless server and give some thought about the resulting energy consumption (but don't want to be bothered, thinking about it too much). It is implemented as a background service, that takes control over sleep management, monitoring a hand-picked list of OS resources and initiates configurable actions accordingly.

## Core Features

Features marked with a construction sign (🚧) are not fully operational yet, but will be released very soon.

### SessionMonitor

- Monitors user sessions, that should keep the system running, while using the computer
    - Takes the standard `LastInputTime` of the Windows session into account
    - Remote Desktop connections are always considered as active
    - Designate process groups of a session with a CPU threshold, that will also count as user activity
- Configure actions to take, when the session logins or becomes idle (lock, logout, disconnect, execute a program/script, ...)

### NetworkMonitor
Utilizes the free [npcap](https://npcap.com/) library to monitor incoming network packets.

- Define arbitrary network services (by port usage) that will stop the PC from suspending, while traffic is registered
- Configure triggers to take actions when a service is accessed or starts to idle

- Support for Hyper-V included
    - Can start virtual machines, when they are accessed by a network service
    - Can stop/suspend virtual machines, when they are not needed anymore
    - Responds with ARP packets on behalf of the suspended virtual machines, to provide a seamless experience

### NetworkSessionMonitor

- Monitors access to shared folders and files, which will stop the system from going to sleep
- Configure rules to include/exclude specific use cases: 
    - Filter by share name
    - Filter by remote username
    - Filter by remote client name
    - Filter by remote IP
    - Filter by file path

### ProcessMonitor

- Designate process groups with an CPU threshold, that will count as system activity

### PowerRequestMonitor

- Create include/exclude filters for the built-in power requests, that should be allowed to keep the system awake


## Additional Features

To excercise the open architecture of the framework, some of the more specific features were developed and packaged as plugins, that can be added and removed any time.

### 🚧 Interactive Taskbar Icon

Incarnates a little helper process in each session, that communicates with the background service to display information and to allow manual control of the sleep cycle.

- Set an indefinite sleepless mode
- Set a time based sleepless mode
- Disable the usage based sleepless mode temporarily
- Observe the reasons, why the system stays awake, in real time

- Lightning fast console session switch, with access control
- Multi-user capable

### DuoStreamMonitor

For those who are enthusiastic users of [DuoStream](https://github.com/DuoStream), this plugin makes Desomnia aware of the configured instances.

- Start instances on demand, when they are accessed by a Moonlight client (no further clientside configuration needed)
- Stop instances after they become idle, to reduce power consumption of the GPU and to reduce the overall footprint of system resources
- Of course the system refrains from sleep until the last session is disconnected

### etc.

If you find, that a cruscial feature is missing, don't hesitate to open an issue and explain why Desomnia should have support for your use case. Alternatively if you are adept at programming in C#, you can check out the provided 🚧 **example project** and develop your own extension plugin, to make Desomnia aware of your needs.

## Observability

The standard configuration writes the reasons for sleep inhibition into a log file. If your server runs unattended for a long time, you can later analyze how often it went into sleep mode and why it didn't.

## System Requirements

- Windows 8 / 10 / 11
- .NET 8 / .NET Framework 4.8
- [npcap](https://npcap.com/) (optional, only needed for NetworkMonitor)

### How to get started

A considerable amount of development time was invested to provide you with a sophisticated installer, that allows you to set everything up and running in a minute.

It does the work for you, to register Desomnia as a system service, download and install all necessary dependencies and guide you through a basic configuration of the parameters. Nevertheless, you are encouraged to dive into the 🚧 [**Wiki**](https://github.com/MadWizardDE/Desomnia/wiki) to discover, what Desomnia can do for you and how to configure it.

If it happens that you don't like Desomnia, the uninstaller will help you to remove everything from your system completely. For your convenience, you can run the installer again (or hit "Modify" in the system settings) to add/remove some of the optional features later on.

🪄 To begin just download the latest release from GitHub and follow the steps of the 🧙 Wizard. 

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://coff.ee/mad0x20wizard)