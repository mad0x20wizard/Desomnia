Windows installer
=================

:OS: 🪟 *Windows*

The easiest way to set up Desomnia on Windows is to download the latest installer from its `Release`_ page on GitHub. It takes care of registering Desomnia as a system service, installs all required dependencies, and walks you through a basic initial configuration.

.. image:: /_static/images/windows/installer.png
   :width: 40em

Once installed, you can run the installer again — or select "Modify" in the system settings — to add or remove optional features at any time.

Optional Features
-----------------

The installer includes all available plugins from the main repository. Some of these have additional requirements:

* :doc:`/plugins/duo/plugin` – requires **Duo** to be installed
* :doc:`/plugins/hyperv` – requires the **Hyper-V Platform** feature to be enabled
* :doc:`/plugins/fko`
* :doc:`/plugins/fritzbox`
* :doc:`/plugins/bridge`

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

Filesystem layout
-----------------

The Windows service uses the following locations. ``%ProgramData%`` normally
expands to ``C:\ProgramData``; paste the paths below into the File Explorer
address bar to open them, even when the directory is hidden.

``%ProgramFiles%\Desomnia``
    Default installation directory for the service executable and its dependencies.
    A different directory can be selected during installation.

``%ProgramFiles%\Desomnia\plugins``
    Plugins selected in the installer, under the installation directory.

``%ProgramData%\Desomnia\config\monitor.xml``
    Monitoring configuration created by the configuration wizard. Edit this file
    to configure monitors, environments, and actions. The service automatically
    reloads changes; startup settings declared with ``<?system ...?>`` require
    a service restart.

``%ProgramData%\Desomnia\config\NLog.config``
    Optional :doc:`logging configuration </concepts/logging>`. Create this file
    beside ``monitor.xml`` to configure log targets and levels, then restart
    the Desomnia service. With ``autoReload="true"``, subsequent changes to this
    logging configuration take effect without a restart.

``%ProgramData%\Desomnia\logs``
    Log output when file logging is enabled in ``NLog.config`` and the targets
    use ``${var:logDir}`` as their base path. Desomnia supplies this directory
    as the default value of ``logDir``.

``%ProgramData%\Desomnia\plugins``
    Additional plugins loaded when the service starts.

Uninstallation
--------------

To remove Desomnia, open "Installed apps" in the system settings, search for "Desomnia", and select "Uninstall". The uninstaller removes all components that the installer placed on your system.

.. _`Release`: https://github.com/mad0x20wizard/Desomnia/releases
