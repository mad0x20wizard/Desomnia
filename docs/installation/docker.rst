Docker container
================

:OS: 🐧 *Linux*

Desomnia can be run inside a `Docker container`_, which isolates the service from the rest of the system and improves portability. It's also the easiest way to get an instance up and running on Linux, as it comes packaged with all the necessary libraries and tools. Only two additional capabilities need to be granted explicitly, as Desomnia requires raw network access in order to function correctly, something that Docker usually prohibits.

There are a couple of directories, that you have to bind mount, to provide the service with your individual configuration and to see what's going on:

.. include:: ./paths.rst

.. note::

  All compatible plugins from the main repository are included by default. You only have to provide versions of your own custom plugins.

Configuration
-------------

This is an example ``docker-compose.yaml`` file, which contains all the configuration settings needed to boot up the Docker container:

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

Place this in the current directory and run ``docker compose up`` to start the container.

.. _`Docker container`: https://hub.docker.com/r/mad0x20wizard/desomnia