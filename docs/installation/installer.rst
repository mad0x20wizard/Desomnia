Windows installer
=================

:OS: 🪟 *Windows*

The easiest way to set up Desomnia on Windows is to download the latest installer from its `Release`_ page on GitHub. It registers Desomnia as a system service, installs all required dependencies, and walks you through a basic initial configuration.

.. image:: /_static/images/installer.png
   :width: 40em

Once installed, you can run the installer again — or select "Modify" in the system settings — to add or remove optional features at any time.

Optional Features
-----------------

The installer includes all available plugins from the main repository. The following plugins have additional requirements:

* :doc:`/plugins/duo/plugin` – requires **Duo** to be installed
* :doc:`/plugins/hyperv` – requires the **Hyper-V Platform** feature to be enabled
* :doc:`/plugins/fko`

Configuration wizard
--------------------

To ease the onboarding process, the installer walks you through a short configuration wizard that covers:

- Whether Desomnia should replace the built-in power management
- Timeouts and delays
- A specific :doc:`network interface </modules/network/interface>` for monitoring
- :doc:`Promiscuous mode </modules/network/promiscuous>`
- Local and remote hosts and services
- :doc:`Single Packet Authorization </modules/network/knocking>` (SPA)
- :doc:`Virtual machines </modules/network/virtual>` to monitor and automate

Uninstallation
--------------

To remove Desomnia, open "Installed apps" in the system settings, search for "Desomnia", and select "Uninstall". The uninstaller removes all components that the installer placed on your system.

.. _`Release`: https://github.com/mad0x20wizard/Desomnia/releases
