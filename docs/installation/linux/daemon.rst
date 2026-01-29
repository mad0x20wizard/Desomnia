Daemon
======

:OS: 🐧 *Linux*

To install Desomnia manually as a systemd daemon onto your system, you have several options available.

Prerequisites
-------------

In order to be able to run Desomnia on your system, you will need the .NET Runtime (< 100 MB in size). You can use this official script, to download everything you need into the default location, where the runtime environment will be found automatically::

    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 -runtime dotnet --install-dir /usr/share/dotnet

For the monitoring of network services the `libpcap`_ library is used, which is usually already installed on most distributions.

Filesystem layout
-----------------

There is nothing wrong in using Desomnia in portable mode with everything residing in the same directory. But for a persistent installation, you are encouraged to use these locations in alignment with the `Filesystem Hierarchy Standard <https://en.wikipedia.org/wiki/Filesystem_Hierarchy_Standard>`_ (FHS) on Unix systems:

/usr/sbin
    Drop the appropriate executable for your platform and architecture into this location, so it can be automatically found. Don't forget to set the necessary executable permission on the file with ``chmod +x /usr/sbin/desomniad``.

.. include:: ./shared_dirs.rst

Service configuration
---------------------

In order for systemd to manage the automatic start and stop of the service, we need to create a service unit, describing how to start the service.

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

    You can use that ``ExecStartPre`` statement, to make sure that the log folder is cleaned every time when the application starts. I use this mostly for debugging purposes.

After you created or changed the configuration file, you have to reload systemd with ``systemctl daemon-reload``. If you want that Desomnia is started with the system, you have to enable it with ``systemctl enable desomnia``. In any case you can start Desomnia with ``systemctl start desomnia`` and stop it gracefully with ``systemctl stop desomnia``.

To see the latest INFO logging, use ``journalctl -u desomnia -f -n 80``. Here you can see which hosts have received a Magic Packet recently and why.

.. _`libpcap`: https://github.com/the-tcpdump-group/libpcap
