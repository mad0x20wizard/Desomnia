Homebrew
========

:OS: 🐧 *Linux* 🍎 *macOS*
    
The easiest way to set up Desomnia on Linux or macOS is to use `Homebrew`_. It enables you to get everything up and running easily, with the following command:

.. code:: bash

   brew install mad0x20wizard/tools/desomnia

The software will be installed according to the standard Homebrew filesystem layout. The root of this layout will be different, depending on the platform you are using. To find your actual *Homebrew prefix*, you can execute the following command:

.. code:: bash

   brew --prefix

For example on macOS running on Apple Silicon, this will result in the path ``/opt/homebrew``. All the following paths will be relative to this base path.

Binary distribution
-------------------

When you install software via Homebrew, the source code is downloaded and the project is built locally by default. The maintainer can also provide pre-built binaries, known as "bottles", which can be downloaded and installed without the need for a build. Desomnia comes `bottled <https://github.com/mad0x20wizard/homebrew-tools/pkgs/container/tools%2Fdesomnia>`__ for the following platforms:

- ``linux/x64``
- ``linux/arm64``
- ``macos/sequoia/arm64``
- ``macos/tahoe/arm64``

Filesystem layout
-----------------

Use these locations to customize your installation of Desomnia, relative to your Hombrew installation:

.. include:: ./paths.rst

Running as background service
-----------------------------

Desomnia can only reach its full potential when run as a background service. Homebrew provides a platform-agnostic method of installing the software as an auto-started service using the following command:

.. code:: bash

   sudo brew services start mad0x20wizard/tools/desomnia

If you want to stop the Homebrew service and unregister it from auto-start, you can do so using the following command:

.. code:: bash

   sudo brew services stop mad0x20wizard/tools/desomnia

.. attention::

    You need to use ``sudo`` here so that Desomnia will be installed and uninstalled as a system service with root privileges.

.. note::

   Although Homebrew suggests to install and run the service the same way on both platforms, a couple of problems can arise, while doing this on 🐧 **Linux**:
   
   - Running the brew command with sudo can fail, if brew is not on the default path of the root user. You can get around this by specifying an absolute path instead:

      .. code:: bash

         sudo /home/linuxbrew/.linuxbrew/bin/brew services start mad0x20wizard/tools/desomnia

   - On some platforms, it seems necessary to configure sudo to preserve the ``HOME`` environment variable in order to run the brew command as root:

      .. code:: bash

         sudo --preserve-env=HOME brew services start mad0x20wizard/tools/desomnia

   Please use either of these fixes or both, to successfully configure Desomnia as a root service on your system.

Plugins included
----------------

If you install Desomnia via Homebrew, it comes with all the plugins of the main repository already included. To install additional plugins, just drop the ZIP file of your plugin into ``.../var/lib/desomnia/plugins``. The file must be in the format ``plugin-*.zip``, with an optional version specifier, separated from the name by ``_``.

Logging
-------

macOS
+++++

You can find the **StandardOutput** and **-Error** logs at ``.../var/log/desomnia``. In order for Desomnia to write additional log files, you must create a :doc:`log configuration </concepts/logging>` at ``.../etc/desomnia/NLog.config``.

Linux
+++++

.. include:: ./journal.rst


Uninstallation
--------------

If you don't like Desomnia, you can install it with the following command:

.. code:: bash

    brew uninstall mad0x20wizard/tools/desomnia

.. note::

   Please note that this doesn't remove either the configuration, custom plugins or logfiles. You will have to remove them manually from their respective directories.

.. _`Homebrew`: https://brew.sh/
