Homebrew
========

:OS: 🐧 *Linux* 🍎 *macOS*

The easiest way to set up Desomnia on Linux or macOS is to use `Homebrew`_. Install it with the following command:

.. code:: bash

   brew install mad0x20wizard/tools/desomnia

The software will be installed according to the standard Homebrew filesystem layout. The root of this layout differs by platform. To find your Homebrew prefix, run:

.. code:: bash

   brew --prefix

On macOS running on Apple Silicon, this is ``/opt/homebrew``. All paths below are relative to this base path.

Binary distribution
-------------------

Homebrew normally downloads source code and builds the project locally. Desomnia also provides pre-built binaries (known as "bottles") for the following platforms:

- ``linux/x64``
- ``linux/arm64``
- ``macos/sequoia/arm64``
- ``macos/tahoe/arm64``

The availability of pre-built bottles can be checked `here <https://github.com/mad0x20wizard/homebrew-tools/pkgs/container/tools%2Fdesomnia>`__.

.. note::

   Homebrew installs the standard build with the bundled plugins supported by that operating system. On a 64-bit Linux device — such as an always-on :doc:`Wake-on-LAN proxy </guides/wol-proxy>`, or wherever a minimal footprint matters more than plugins — consider the self-contained **native build** instead. It needs no .NET runtime and uses far less memory, but drops runtime plugin loading. It is not distributed through Homebrew; obtain it from the release archive (see :doc:`manual installation </installation/manually>`) or as the ``-native`` :doc:`Docker image </installation/docker>`. See :doc:`/modules/network/performance` for the trade-offs.

Filesystem layout
-----------------

Use these locations to customise your installation of Desomnia, relative to your Homebrew installation:

.. include:: ./paths/data.rst

Running as background service
-----------------------------

Desomnia can only reach its full potential when run as a background service. Homebrew provides a platform-agnostic way to install and auto-start it:

.. code:: bash

   sudo brew services start mad0x20wizard/tools/desomnia

To stop the service and remove it from auto-start:

.. code:: bash

   sudo brew services stop mad0x20wizard/tools/desomnia

.. attention::

    ``sudo`` is required so that Desomnia is installed and run as a system service with root privileges.

.. error::

   A couple of problems can arise when running this on 🐧 **Linux**:

   - Running the brew command with ``sudo`` can fail if brew is not on the default path of the root user. Specify an absolute path to work around this:

      .. code:: bash

         sudo /home/linuxbrew/.linuxbrew/bin/brew services start mad0x20wizard/tools/desomnia

   - On some platforms, it is necessary to configure sudo to preserve the ``HOME`` environment variable:

      .. code:: bash

         sudo --preserve-env=HOME brew services start mad0x20wizard/tools/desomnia

   Use either or both of these workarounds as needed.

Plugins included
----------------

Homebrew installations include the bundled plugins supported on Linux/macOS, including Firewall Knock Operator and FRITZ!Box. Windows-only integrations such as Duo and Hyper-V are not available on these platforms. To install additional plugins, place the ZIP file in ``.../var/lib/desomnia/plugins``. The file must follow the naming convention ``plugin-*.zip``, with an optional version specifier separated from the name by ``_``.

Logging
-------

The location of the default output of Desomnia depends on the platform:

File 
++++

:OS: 🍎 macOS

The standard output and error logs, as well as the extended logs are written to ``.../var/log/desomnia``. 

Journal
+++++++

:OS: 🐧 Linux

.. include:: ./journal.rst


Extended Logging
----------------

To enable additional file-based logging, create a :doc:`log configuration </concepts/logging>` at ``.../etc/desomnia/NLog.config``. Unless configured otherwise, these logs will be written to ``.../var/log/desomnia``.

Uninstallation
--------------

To remove Desomnia from your system:

.. code:: bash

    brew uninstall mad0x20wizard/tools/desomnia

.. note::

   This does not remove the configuration, custom plugins, or log files. Those must be deleted manually from their respective directories.

.. _`Homebrew`: https://brew.sh/
