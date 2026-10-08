Native packages
===============

:OS: 🐧 *Linux*

Desomnia is distributed as native ``.deb`` and ``.rpm`` packages for the most common Linux
distributions. This is the recommended way to install it on a headless Linux server or
single-board computer: the package registers Desomnia as a systemd service, pulls in the
required system libraries, and follows the standard filesystem layout.

The packages contain the **native, ahead-of-time compiled build** — a single self-contained
binary that needs no .NET runtime and uses far less memory (around 48 MB instead of ~130 MB),
which makes it a good fit for an always-on :doc:`Wake-on-LAN proxy </guides/wol-proxy>`. See
:doc:`/modules/network/performance` for the reasoning.

.. note::

   Because this is the native build, plugins cannot be loaded at runtime: the
   :doc:`Firewall Knock Operator </plugins/fko>` and the
   :doc:`FRITZ!Box </plugins/fritzbox>` plugin are built in, but other
   plugins are not. If you
   need additional plugins, install the full build via :doc:`Homebrew </installation/homebrew>`,
   the :doc:`archive </installation/manually>`, or :doc:`Docker </installation/docker>` instead.
   Everything else — including :doc:`local sleep management </guides/sleep>` via
   ``systemd-logind`` — works as in the full build, so the package also suits hosts that should
   sleep themselves.

Supported platforms
--------------------

Packages are published for the following architectures:

- ``amd64`` / ``x86_64`` — 64-bit Intel/AMD
- ``arm64`` / ``aarch64`` — 64-bit ARM, e.g. a Raspberry Pi 3/4/5 running a 64-bit OS

They require **glibc 2.35 or newer** — Debian 12 "Bookworm", Ubuntu 22.04, Raspberry Pi OS
(Bookworm), a current Fedora/openSUSE, or later. For 32-bit ARM, other architectures, or older
systems, use the :doc:`archive </installation/manually>` or :doc:`Docker </installation/docker>`
installation.

Installation
------------

The recommended way is to register the Desomnia **package repository** once — ``apt`` or ``dnf``
then installs *and updates* Desomnia like any other system package. Alternatively, individual
packages can be downloaded from the repository page and installed directly, without automatic
updates.

The repository is hosted by `Cloudsmith`_. The setup script below detects your distribution,
imports the repository signing key, and registers the package source; if you prefer to
configure the source by hand, the repository page linked above provides per-distribution
instructions.

Debian, Ubuntu, Raspberry Pi OS
+++++++++++++++++++++++++++++++

Register the repository, then install:

.. code:: bash

   curl -1sLf 'https://dl.cloudsmith.io/public/mad0x20wizard/tools/setup.deb.sh' | sudo -E bash
   sudo apt install desomnia

Desomnia is then kept up to date by ``apt upgrade``, together with the rest of the system.

Fedora, RHEL, openSUSE
++++++++++++++++++++++

Register the repository, then install:

.. code:: bash

   curl -1sLf 'https://dl.cloudsmith.io/public/mad0x20wizard/tools/setup.rpm.sh' | sudo -E bash
   sudo dnf install desomnia

On openSUSE, install with ``zypper install desomnia`` instead.

Direct download
+++++++++++++++

To install a package without registering the repository, download the file matching your format
and architecture from the `Cloudsmith`_ repository page and install it directly — dependencies
are still resolved automatically, but newer versions have to be downloaded and installed
manually:

.. code:: bash

   sudo apt install ./Desomnia_<version>_linux-<arch>-native.deb   # Debian, Ubuntu, Raspberry Pi OS
   sudo dnf install ./Desomnia_<version>_linux-<arch>-native.rpm   # Fedora, RHEL, openSUSE

.. note::

   Only **stable releases** are packaged. To try an alpha or beta version, use the archive from
   the `GitHub Releases`_ page as described in the :doc:`manual installation </installation/manually>`.

.. note::

   The ``libpcap`` library is required and pulled in automatically. The optional tools
   ``ethtool`` and ``iproute2`` — used to arm the network interface for Wake-on-LAN and to
   assign temporary address mappings — are recommended and installed alongside on most systems.

Filesystem layout
-----------------

Desomnia uses the following locations in alignment with the `Filesystem Hierarchy Standard`_ (FHS):

.. include:: ./paths/bin.rst
   :end-before: .. permissions ..

.. include:: ./paths/data.rst
   :end-before: .. plugins ..

/usr/lib/systemd/system/desomnia.service
    The systemd service unit.

Configuration
-------------

The package installs a ready-to-run configuration at ``/etc/desomnia/monitor.xml``, which is
why the service can run immediately after installation. On the first installation, the
configuration is chosen to match the machine's role:

- **Always-on box** — if the machine cannot suspend, or no network adapter can wake it via
  Wake-on-LAN (typical for single-board computers like a Raspberry Pi), Desomnia starts in the
  zero-configuration :doc:`Sleep Proxy </modules/network/sleepproxy>` mode: it watches the
  network in promiscuous mode and keeps sleeping hosts reachable, waking them on demand,
  without any per-host setup — the always-on :doc:`Wake-on-LAN proxy </guides/wol-proxy>` role.
- **Sleep-capable host** — if the machine can suspend *and* has a Wake-on-LAN capable adapter,
  a local configuration is installed instead: Desomnia watches only the host's own traffic (no
  promiscuous mode, no proxy role), prepared for :doc:`local sleep management </guides/sleep>`.
  Suspending on idle stays off until you configure it.

The choice is made **only on the first installation** — upgrades never touch ``monitor.xml``.
Both templates ship under ``/usr/share/desomnia/`` (``monitor-proxy.xml`` and
``monitor-host.xml``); to switch roles later, copy the one you want over
``/etc/desomnia/monitor.xml``; the packaged service reloads it automatically.

To tailor it to your network — declaring specific hosts, a router, or a Wake-on-LAN client role —
edit the file:

.. code:: bash

   sudo nano /etc/desomnia/monitor.xml


The packaged service enables ``--auto-reload`` (``-a``), so changes to
``monitor.xml`` take effect without a manual restart. Settings declared with
``<?system ...?>``, such as process polling,
are startup settings and require a service restart. To disable automatic reload,
override the service's ``ExecStart`` without ``-a``.

A configuration that cannot be loaded can stop the application; check
``journalctl -u desomnia``, fix the file, and restart if necessary.
See :doc:`/guides/troubleshooting`.

Your changes are preserved across package upgrades. See :doc:`/concepts/resources` for the full
set of configuration elements.

Running as a service
--------------------

Because a working configuration ships with the package, the systemd service is enabled and
started **automatically** on installation. Check its status at any time:

.. code:: bash

   systemctl status desomnia

To stop it and prevent it from starting at boot:

.. code:: bash

   sudo systemctl disable --now desomnia

Journal
+++++++

.. include:: ./journal.rst

Updating
--------

When installed from the repository, Desomnia is updated by the regular system upgrade
(``apt upgrade`` / ``dnf upgrade``). For a directly downloaded package, download the newer
package and install it the same way. In both cases, if the service was running it is restarted
automatically, and your ``monitor.xml`` is left untouched.

Uninstallation
--------------

.. code:: bash

   sudo apt remove desomnia      # Debian/Ubuntu
   sudo dnf remove desomnia      # Fedora/RHEL

This stops and disables the service. Your configuration in ``/etc/desomnia`` and any log files
are left in place; remove them manually if you no longer need them. If you registered the
package repository, the source file created by the setup script
(``/etc/apt/sources.list.d/mad0x20wizard-tools.list`` or
``/etc/yum.repos.d/mad0x20wizard-tools.repo``) can be removed as well.

.. note::

   Package repository hosting is graciously provided by `Cloudsmith`_, the only fully hosted,
   cloud-native, universal package management solution.

.. _`Cloudsmith`: https://cloudsmith.io/~mad0x20wizard/repos/tools/packages/

.. _`GitHub Releases`: https://github.com/mad0x20wizard/Desomnia/releases

.. _`Filesystem Hierarchy Standard`: https://en.wikipedia.org/wiki/Filesystem_Hierarchy_Standard
