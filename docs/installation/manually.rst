Manually
========

:OS: 🐧 *Linux*

Follow this guide to set up Desomnia on Linux manually, with full control over the binary placement and service configuration.

Prerequisites
-------------

Desomnia requires the .NET Runtime. Use the official script to download and install it into the default location, where it will be found automatically::

    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 -runtime dotnet --install-dir /usr/share/dotnet

For monitoring network services, the `libpcap`_ library is required. It is usually already present on most distributions. If it is missing, install it via your package manager. On Debian-based distributions (Ubuntu, Raspbian, etc.)::

    apt-get install libpcap-dev

Portable mode
-------------

To verify that Desomnia is compatible with your system and network infrastructure before committing to a full installation, you can run it in portable mode.

Download the appropriate binary for your platform from the `GitHub Releases <https://github.com/mad0x20wizard/Desomnia/releases>`_ page. Set the executable permission with ``chmod +x ./desomniad``, then create a suitable configuration file and place it next to the binary. Additional plugin archives can be placed in a subdirectory named ``plugins``. Run the application as root with ``./desomniad``.

Filesystem layout
-----------------

For a persistent installation, use the following locations in alignment with the `Filesystem Hierarchy Standard`_ (FHS):

/usr/sbin
    Drop the binary for your platform and architecture here so it can be found automatically. Set the executable permission with ``chmod +x /usr/sbin/desomniad``.

.. include:: ./paths.rst

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

    The ``ExecStartPre`` statement clears the log folder on each start, which is useful for debugging.

Starting and stopping
+++++++++++++++++++++

After creating or modifying the configuration file, reload systemd with ``systemctl daemon-reload``. To start Desomnia automatically with the system, enable it with ``systemctl enable desomnia``. Start the service with ``systemctl start desomnia`` and stop it gracefully with ``systemctl stop desomnia``.

Journal
+++++++

.. include:: ./journal.rst

.. _`libpcap`: https://github.com/the-tcpdump-group/libpcap

.. _`Filesystem Hierarchy Standard`: https://en.wikipedia.org/wiki/Filesystem_Hierarchy_Standard
