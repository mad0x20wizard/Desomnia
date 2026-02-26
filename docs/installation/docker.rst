Docker container
================

:OS: 🐧 *Linux*

Desomnia can be run inside a Docker container, to isolate the service from the rest of the system and also to improve portability. It's also the easiest way to get an instance up and running, as it comes packaged with all the necessary libraries and tools. Only two additional capabilities need to be granted explicitly, as Desomnia requires raw network access in order to function correctly, something that Docker usually prohibits.

There are two directories, that you have to bind mount, to provide the service with your individual configuration and to see what's going on:

.. include:: ./paths.rst

This is an example ``docker-compose.yaml`` file, which contains all the configuration settings needed for the Docker container:

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
