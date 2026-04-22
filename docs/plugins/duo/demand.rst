Demand detection
================

When a Moonlight client attempts to connect to an instance that is not yet running, Desomnia can intercept that connection attempt and start the instance for you. Two modes are available:

- **Packet capturing** (recommended): Desomnia uses the NetworkMonitor's passive packet capturing ability to detect incoming TCP connection attempts on each instance's base port. This requires Npcap to be installed, which is a dependency of the NetworkMonitor and will be present on any system where the full Desomnia setup is in use.
- **TCP listener fallback**: If Npcap is not available, Desomnia opens a lightweight TCP listener on each instance's base port instead. When a connection arrives, the listener closes and the instance is started. Once the instance is up, Desomnia uses native Windows API calls to monitor active connections and detect when no clients remain.

  .. note::

     In fallback mode, incoming connections on the instance base ports must be permitted by the Windows Firewall. Desomnia adds the required inbound rules automatically when it starts and removes them again when it shuts down.
