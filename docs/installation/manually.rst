Manually
========

:OS: 🐧 *Linux*

Follow this guide to set up Desomnia on Linux manually, with full control over the binary placement and service configuration.

Prerequisites
-------------

Desomnia requires the **.NET Runtime**. If it is not provided as a `package <https://learn.microsoft.com/en-us/dotnet/core/install/linux>`_ by your distribution, use the official script to download and install it into the default location, where it will be found automatically::

    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 -runtime dotnet --install-dir /usr/share/dotnet

For monitoring network services, the `libpcap`_ library is required. It is usually already present on most distributions. If it is missing, install it via your package manager. On Debian-based distributions (Ubuntu, Raspbian, etc.)::

    apt-get install libpcap0.8t64

In order to automatically configure your network interface for **Wake-on-LAN** and temporarily assign static address mappings, Desomnia additionally needs the following optional network tools::

    apt-get install ethtool iproute2

Native build
------------

For 64-bit systems, a **native build** is offered in addition to the standard one — the ``Desomnia_<version>_linux-<arch>-native.zip`` asset on the `GitHub Releases <https://github.com/mad0x20wizard/Desomnia/releases>`_ page. It is compiled ahead of time into a single self-contained binary that **does not require the .NET Runtime**, so you can skip the runtime step under `Prerequisites`_ above and simply place the binary. It also uses considerably less memory — around 48 MB instead of ~130 MB — which makes it a good fit for always-on devices such as a Raspberry Pi acting as a Wake-on-LAN or Sleep Proxy. See :doc:`/modules/network/performance` for the reasoning.

.. include:: ./native/constraints.rst

The ``libpcap`` library and the optional network tools above are still required to install; only the .NET Runtime becomes unnecessary.

Portable mode
-------------

To verify that Desomnia is compatible with your system and network infrastructure before committing to a full installation, you can run it in portable mode.

Download the appropriate binary for your platform from the `GitHub Releases <https://github.com/mad0x20wizard/Desomnia/releases>`_ page. Set the executable permission with ``chmod +x ./desomniad``, then create a suitable configuration file and place it next to the binary. Additional plugin archives can be placed in a subdirectory named ``plugins``. Run the application as root with ``./desomniad``.

Filesystem layout
-----------------

For a persistent installation, use the following locations in alignment with the `Filesystem Hierarchy Standard`_ (FHS):

.. include:: ./paths/bin.rst
.. include:: ./paths/data.rst

Service configuration
---------------------

Create a systemd service unit to manage automatic start and stop:

/etc/systemd/system/desomnia.service
    .. code:: ini

        [Unit]
        Description=Desomnia

        [Service]
        ExecStartPre=find /var/log/desomnia/ -type f -delete
        ExecStart=desomniad
        Restart=always
        RestartSec=5

        [Install]
        WantedBy=multi-user.target

    Adjust the executable path to its installed location. The ``-a`` option enables
    automatic configuration reload. Logs are retained across restarts for troubleshooting.

Starting and stopping
+++++++++++++++++++++

After creating or modifying the service unit, reload systemd with ``systemctl daemon-reload``. Changes to ``monitor.xml`` are reloaded by Desomnia when ``-a`` is enabled; startup settings in ``<?system ...?>`` still require restarting the service. To start Desomnia automatically with the system, enable it with ``systemctl enable desomnia``. Start the service with ``systemctl start desomnia`` and stop it gracefully with ``systemctl stop desomnia``.

Journal
+++++++

.. include:: ./journal.rst

.. _`libpcap`: https://github.com/the-tcpdump-group/libpcap

.. _`Filesystem Hierarchy Standard`: https://en.wikipedia.org/wiki/Filesystem_Hierarchy_Standard
