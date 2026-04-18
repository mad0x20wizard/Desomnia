The INFO-level console output shows the most important actions Desomnia takes — such as waking remote hosts or sending knock packets — giving a quick overview of what the service is doing at any point in time. Warnings and errors are included as well.

To follow the live output from the systemd journal, run:

.. code:: bash

   journalctl -u desomnia -f -n 80
