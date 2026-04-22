Knocking
========

Single Packet Authorization (SPA) is a lightweight authentication mechanism that lets a client prove its identity to a server with a single UDP packet, before any connection is attempted. Unlike a conventional login where authentication happens after the connection is established, SPA reverses this order: the server remains completely silent — it does not respond and gives no indication that any service is running — unless and until it receives a valid knock.

In Desomnia, SPA serves a specific purpose: protecting the wake-up mechanism from being triggered by unsolicited traffic arriving from outside the local network. A port scanner or automated bot probing your router's public IP will receive no response and cannot wake your hosts. A legitimate client that sends a valid knock has its IP temporarily authorized, and any connection attempt from that IP to a watched host during the authorization window can trigger a wake-up.

.. note::

   SPA authenticates *access* — it does not encrypt your data traffic. If the connection itself needs to be private, combine SPA with application-level encryption such as SSH or TLS.

How it works
------------

The exchange involves two sides: the **sender** (the client connecting from outside) and the **receiver** (the always-on device running Desomnia in proxy mode inside your network).

Sender side
+++++++++++

When the sender wants to connect to a service on a sleeping host, it first transmits a single UDP packet — the knock — to a configured port on the receiver. The knock packet contains credentials (a shared secret or encrypted payload, depending on the method) that the receiver can validate. The sender then waits briefly and proceeds with the connection attempt, trusting that the receiver has processed the knock.

Desomnia's built-in knock sender can automate this entirely: when ``onServiceDemand="knock"`` is configured on a remote host, Desomnia detects the outgoing connection attempt, sends the knock automatically, and forwards the connection once the target host is awake. No manual step is required.

Receiver side
+++++++++++++

The receiver does not open a listening socket on the knock port. Instead, it captures knock packets passively, the same way it captures other network traffic. This is significant: a port scan will find the knock port closed, because there is nothing listening on it in the conventional sense.

When a knock packet arrives, the receiver validates it. If validation passes, the sender's IP address is added to a temporary trusted range for the duration of ``knockTimeout``. Any connection attempt from that IP to a watched host during this window can trigger a wake-up. When the window expires, the IP is removed and access is revoked automatically.

If validation fails — wrong secret, expired timestamp, malformed packet — the receiver discards the packet silently. Nothing is sent back.

On the receiver, the SPA listener is configured through a ``<DynamicHostRange>`` element placed inside a ``<ForeignHostFilterRule>``. This single element holds the knock method, port, timeout, validation options, and shared secrets. When a valid knock arrives, the sender's IP is dynamically added to that range for the duration of the authorization window.

Knock methods
-------------

Two knock methods are available, with different security properties.

Plain Text
++++++++++

The ``plain`` method transmits the shared secret as a UTF-8 string inside the UDP payload. There is no encryption and no replay protection. Anyone who captures the packet can read the secret and resend it.

This method protects against port scanners and automated bots, which do not know the knock sequence and have no reason to look for it. It does not protect against a targeted attacker who can observe traffic on the network path between sender and receiver.

Firewall Knock Operator
+++++++++++++++++++++++

The ``fko`` method is based on the open `fwknop <https://github.com/mrash/fwknop>`_ protocol (Firewall Knock Operator). The payload is encrypted with AES and can be authenticated with an HMAC, making it resistant to both eavesdropping and message forgery. Optionally, a timestamp can be embedded in the payload: when ``proofTime`` is configured on the receiver, a captured packet becomes invalid once its timestamp falls outside the configured acceptance window, preventing replay attacks.

The ``fko`` method is interoperable with the original fwknop client, so Desomnia can receive knocks from any fwknop-compatible sender, and vice versa.

See :doc:`/plugins/fko` for configuration details and the full list of supported fwknop features.

Comparison
++++++++++

.. list-table::
   :header-rows: 1
   :widths: 40 20 20

   * - Feature
     - ``plain``
     - ``fko``
   * - Payload encryption
     - ❌ No
     - ✅ AES (128 / 192 / 256-bit)
   * - Replay protection (``proofTime``)
     - ❌ No
     - ✅ Optional (timestamp)
   * - Message authentication (HMAC)
     - ❌ No
     - ✅ Optional but recommended
   * - fwknop interoperability
     - ❌ No
     - ✅ Yes
   * - Configuration complexity
     - Low
     - Medium
   * - Suitable for internet-facing exposure
     - ⚠️ Limited
     - ✅ Yes

Use ``plain`` for testing, or in scenarios where the knock traffic cannot be observed by an adversary (for example, on a trusted internal network). For any deployment exposed to the internet for an extended period, use ``fko``.

Configuration options
-----------------------

Knock behaviour is configured on both sides. On the **receiver**, all SPA settings belong to the ``<DynamicHostRange>`` element:

.. code:: xml

   <ForeignHostFilterRule>
     <DynamicHostRange name="trusted"
       knockMethod="plain"
       knockPort="62201"
       knockTimeout="30s">
       <SharedSecret encoding="UTF-8">my-passphrase</SharedSecret>
     </DynamicHostRange>
   </ForeignHostFilterRule>

On the **sender**, knock attributes are set on ``<RemoteHost>``, with ``<NetworkMonitor>`` available as an inherited default and individual ``<Service>`` elements available for per-service overrides.

The key attributes are:

``knockMethod``
  The knock method to use: ``plain`` or ``fko``. Must match on sender and receiver.

``knockPort``
  The UDP port the receiver listens on and the sender targets. Must match on both sides. Forward this port on your router to make it reachable from outside the network.

``knockTimeout``
  On the receiver: how long the sender's IP remains authorized after a successful knock. Should be long enough for the target host to wake up and accept connections — typically 20–60 seconds depending on your hardware.

  On the sender: the total time Desomnia will keep trying to send the knock. Should be at least as long as the receiver's ``knockTimeout``.

``knockDelay``
  On the sender: how long to wait after detecting a connection attempt before sending the knock.

``knockRepeat``
  On the sender: if set, Desomnia resends the knock at this interval until ``knockTimeout`` expires, as a guard against occasional packet loss.

Knock attributes can be set at the ``<NetworkMonitor>`` level as inherited defaults, overridden at the ``<RemoteHost>`` level, or further overridden at the ``<Service>`` level.

Receiver-only 
+++++++++++++

Some options are only valid in the context of configuring the SPA receiver:

``proofIP``
  On the receiver: when set to ``true``, Desomnia checks that the source IP address observed in the knock packet matches the IP embedded in the SPA payload. This ensures that nobody has tampered with the packet's source address in transit. This check works correctly when the sender is directly reachable from the internet. For senders behind NAT, the embedded IP is the sender's private address, which will not match the public IP observed by Desomnia — support for NAT traversal via external IP lookup is planned for a future release.

``proofTime``
  On the receiver: a duration that specifies the acceptable deviation between the timestamp embedded in the SPA payload and the receiver's current time. When set, any knock whose timestamp falls outside this window is rejected, preventing replay attacks. The value should be generous enough to account for clock differences between sender and receiver — a few minutes is typically sufficient. Supported by ``fko`` only; ``plain`` carries no timestamp.


For a complete worked example, see :doc:`/guides/remote-access`.

Shared secret management
-------------------------

Shared secrets are declared as ``<SharedSecret>`` child elements directly inside the ``<DynamicHostRange>``. Two forms are available:

A simple inline secret, suitable for ``plain`` or as a quick starting point:

.. code:: xml

   <SharedSecret label="simple" encoding="UTF-8">my-passphrase</SharedSecret>

A key pair for ``fko``, consisting of an encryption key and a separate HMAC authentication key:

.. code:: xml

   <SharedSecret label="secure" encoding="Base64">
     <Key>RqBObjFUM9lguaZin1CjJEK0a4FQamAB9ivXHq0/z6w=</Key>
     <AuthKey type="SHA256">AsJ0GS2IMgqbCf1hc9BfKpCK5vXiXs/J2ZLri+XdHCdZsarOTPTbPnwGT1bu7Q5+yjOlnK5oNHe3zyJf7A9J1g==</AuthKey>
   </SharedSecret>

Multiple ``<SharedSecret>`` entries can be declared inside a single ``<DynamicHostRange>``. Desomnia will try each one in order and accept the knock if any of them validates successfully. This makes it possible to rotate keys without downtime: add the new secret alongside the old one, update the sender, then remove the old secret once all clients have migrated. Each secret carries a ``label`` attribute that identifies it in log output.

See :ref:`generating-cryptographically-strong-keys` for instructions on generating suitable random keys.

.. _generating-cryptographically-strong-keys:

Generating keys
---------------

Cryptographically strong keys are required for the ``fko`` method. The following tools produce suitable random keys in Base64-encoded format.

Using OpenSSL
+++++++++++++

:OS: 🐧 *Linux* 🍎 *macOS*

.. code:: bash

    openssl rand -base64 32

This generates a 256-bit (32-byte) key and writes it in Base64-encoded format to the terminal:

.. code::

    1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs=

Via PowerShell
++++++++++++++

:OS: 🪟 *Windows*

.. code:: PowerShell

    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [Convert]::ToBase64String($bytes)

This also produces a suitable Base64-encoded string in your terminal.

Run the command twice to generate a separate encryption key and HMAC authentication key, as recommended by the ``fko`` method.
