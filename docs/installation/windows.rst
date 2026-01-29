Windows
=======

On Windows, Desomnia runs as an automatically started local system service with maximum privileges, to be able to monitor the configured resources. You can start and stop the service via the Service Control Manager (SCM).

For the monitoring of network services the `npcap`_ library needs to be installed, which will be downloaded and installed automatically, when you use the installer.

.. toctree::
   windows/installer
   windows/winget

.. _`npcap`: https://npcap.com/