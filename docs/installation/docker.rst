Docker
======

:OS: 🐧 *Linux*

Desomnia can be run inside a `Docker container`_, which isolates the service from the rest of the system and improves portability. It is also the easiest way to get an instance running on Linux, as it comes packaged with all necessary libraries and plugins. Only two additional capabilities need to be granted explicitly, as Desomnia requires raw network access — something Docker restricts by default.

Filesystem layout
-----------------

The following directories inside the container must be bind-mounted from the host to provide your configuration and access log output:

.. include:: ./paths.rst

Configuration
-------------

The following ``docker-compose.yaml`` contains all the settings needed to start the container:

.. code:: yaml

  services:
    desomnia:
      image: mad0x20wizard/desomnia

      volumes:
        - ./config:/etc/desomnia
        - ./plugins:/var/lib/desomnia/plugins # optional
        - ./logs:/var/log/desomnia # optional

      restart: unless-stopped

      network_mode: host

      cap_add:
        - NET_RAW
        - NET_ADMIN

Place this file in the current directory and run ``docker compose up`` to start the container. The host paths on the left side of each volume mapping (e.g. ``./config``) are relative to the directory where the compose file lives; the right side shows the path inside the container.

.. _`Docker container`: https://hub.docker.com/r/mad0x20wizard/desomnia
