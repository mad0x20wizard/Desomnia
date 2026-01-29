Docker container
================

:OS: 🐧 *Linux*

Desomnia can be run inside a Docker container, to isolate the service from the rest of the system and also to improve portability. It's also the easiest way to get an instance up and running. Two additional capabilities must be granted explicitly because Desomnia requires raw network access to function correctly.

There are two directories, that you have to bind mount, to provide the service with your individual configuration and to see what's going on:

.. include:: ./shared_dirs.rst

This is an example ``docker-compose.yaml`` file, which contains all the configuration settings needed for the Docker container:

.. code:: yaml

    services:
      desomnia:
        image: mad0x20wizard/desomnia

        volumes:
          - ./config:/etc/desomnia
          - ./logs:/var/log/desomnia

        restart: unless-stopped

        network_mode: host

        cap_add:
          - NET_RAW
          - NET_ADMIN
