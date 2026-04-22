Troubleshooting
===============

When Desomnia is not behaving as expected, enabling :doc:`logging </concepts/logging>` is always the right first step. By default Desomnia produces minimal output; a configured log file will show which resources it is tracking, what activity it is detecting, and which actions it is executing — making most problems immediately apparent.

Network interface cannot be found
---------------------------------

:OS: 🪟 *Windows*

When Npcap is installed, it registers itself as a filter driver on all physical network interfaces. Installing a new network adapter — including the virtual switch created by Hyper-V — or reconfiguring an existing one can cause the Npcap driver to be disabled or not yet attached to the underlying physical interface. When this happens, Desomnia cannot capture packets on that adapter and will fail to detect any network traffic.

The setting is not exposed in the modern Windows Settings app or in Device Manager. It can only be reached through the classic network adapter properties dialog:

1. Open **Control Panel → Network and Internet → Network Connections** (or run ``ncpa.cpl``).
2. Right-click the **physical** network adapter and choose **Properties**.
3. In the list of installed network features, locate **Npcap Packet Driver (NPCAP)**.
4. Make sure the checkbox next to it is enabled.
5. Click **OK** and restart Desomnia.

.. figure:: /_static/images/windows/interface.png
   :alt: Windows network adapter properties dialog showing the Npcap Packet Driver checkbox
   :width: 363px

   The Npcap Packet Driver entry must be checked on the physical adapter.

.. note::

   In a Hyper-V setup, the relevant adapters are the **physical** one that the virtual switch is bridged to and the **virtual** adapter that carries the IP configuration. Both need to have the Npcap Filter Driver enabled. See :doc:`/plugins/hyperv` for how Desomnia selects the capture interface in that scenario.
