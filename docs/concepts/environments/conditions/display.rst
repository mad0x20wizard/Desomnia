Display
=======

:OS: 🪟 *Windows* 🍎 *macOS*

Display conditions select settings according to the laptop lid state.

lid
---

Matches the lid state:

``open``
    The laptop lid is open.
``closed``
    The laptop lid is closed.

A machine without a lid, or with an unavailable lid state, matches neither
value. Desomnia logs a warning when the state is unavailable.

.. rubric:: Docking example

The following fragment adds display monitoring while a wired connection is
operational, mains power is available, and the laptop lid is closed:

.. code:: xml

   <Environment name="Docked" interface="Ethernet@up" power="ac" lid="closed">
     <DisplayMonitor preventIdle="enabled" />
   </Environment>

All three conditions must match. ``Ethernet`` is the Windows adapter name in
this example. On macOS, use the dock adapter's interface name, such as ``en5``.
Connected external displays then contribute activity according to the
:doc:`display configuration </modules/display/config>`.

The operating system's lid-close sleep policy applies independently of these
conditions. An environment condition does not prevent forced suspension when
the lid closes.
