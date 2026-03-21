Windows installer
=================

:OS: 🪟 *Windows*

The easiest way to setup Desomnia on Windows is to download the latest installer from it's `Release`_ page on GitHub, which allows you to set everything up and running in a minute.

.. image:: /_static/images/installer.png
   :width: 40em

It does the work for you, to register Desomnia as a system service, download and install all necessary dependencies and guide you through a basic configuration of the parameters. For your convenience, you can run the installer again (or hit “Modify” in the system settings) to add/remove some of the optional features later on.

Optional Features
-----------------

The installer includes all the available plugins from the main repository. You can install these plugins as needed, provided your system meets the necessary requirements:

* :doc:`/plugins/duo/plugin` – needs **Duo** to be installed
* :doc:`/plugins/hyperv` – needs the **Hyper-V-Platform** feature enabled
* :doc:`/plugins/fko`

Configuration wizard
--------------------

To ease the onboarding process, the installer will ask you a few questions to create an initial configuration file. This will allow you to configure the following options:

- Decide if Desomnia should replace the built-in power management
- Set timeouts and delays
- Select a specific :doc:`network interface </modules/network/interface>` for monitoring
- Enable :doc:`Promiscuous mode </modules/network/promiscuous>`
- Configure local and remote hosts/services
- Instruct Desomnia to perform :doc:`Single Packet Authorization </modules/network/knocking>` (SPA)
- Add :doc:`virtual machines </modules/network/virtual>` to monitor and automate

Uninstallation
--------------

If it happens that you don’t like Desomnia, the uninstaller will help you to remove everything from your system completely. Just open the "Installed apps" in the system settings, search for "Desomnia" and hit "Uninstall".

.. _`Release`: https://github.com/mad0x20wizard/Desomnia/releases