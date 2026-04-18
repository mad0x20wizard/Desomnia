Verifying Wake-on-LAN capabilities
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

To verify that Wake-on-LAN is working on a target host independently of Desomnia, you can send a Magic Packet manually using the ``wakeonlan`` utility, available for Linux and macOS via common package managers:

.. code:: bash

   # Debian / Ubuntu
   sudo apt install wakeonlan

   # macOS
   brew install wakeonlan

   wakeonlan 00:1A:2B:3C:4D:5E

If the host wakes up, its firmware and network adapter are configured correctly. If it does not, the issue lies with the target host's configuration rather than with Desomnia.
