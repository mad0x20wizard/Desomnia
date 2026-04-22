Firewall Knock Operator
=======================

:OS: 🪐 *Platform-independent*

The ``fko`` knock method is Desomnia's implementation of the `fwknop`_ (Firewall Knock Operator) protocol. It extends the basic SPA mechanism described in :doc:`/modules/network/knocking` with AES payload encryption, HMAC message authentication, and optional timestamp-based replay protection — making it suitable for deployments that are exposed to the internet for any length of time.

Desomnia is interoperable with the original fwknop tooling: it can receive knocks from an external fwknop client, and its built-in knock sender can target an original fwknop server.

For a more general overview and a comparison with the ``plain`` method, see :doc:`/modules/network/knocking`.

Specifying keys
---------------

The fwknop protocol uses up to two keys. The first encrypts the SPA payload so that an observer cannot read the secret. The second authenticates the message — it makes tampering or forging a valid knock packet computationally infeasible, even for an attacker who knows the encryption key. Using both is strongly recommended.

.. attention::

   Keys must be cryptographically random. See :ref:`generating-cryptographically-strong-keys` for instructions specific to your platform. Run the command twice to produce a separate encryption key and authentication key.

Receiver
++++++++

On the receiver, both keys are declared as a ``<SharedSecret>`` child of the ``<DynamicHostRange>``. The ``<Key>`` element holds the encryption key; ``<AuthKey>`` holds the HMAC key and specifies the digest algorithm via its ``type`` attribute:

.. code:: xml

   <ForeignHostFilterRule>
     <DynamicHostRange name="trusted-clients" knockMethod="fko" knockPort="62201" knockTimeout="30s">
       <SharedSecret label="primary" encoding="Base64">
         <Key>1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs=</Key>
         <AuthKey type="SHA256">1k00+6JhD4LIKAqkVtLN6FCEHZxQamCbGqD+vyCmPjTzALzLLxatBB1tCYdDe4flf+xIqlwP6JpVHwggEk0jqA==</AuthKey>
       </SharedSecret>
     </DynamicHostRange>
   </ForeignHostFilterRule>

Sender
++++++

When Desomnia is also the knock sender, the same keys are supplied as flat attributes on ``<RemoteHost>``. ``knockSecret`` carries the encryption key, ``knockSecretAuth`` carries the HMAC key, and ``knockSecretAuthType`` names the digest algorithm — matching the ``type`` attribute of the receiver's ``<AuthKey>``:

.. code:: xml

   <RemoteHost name="home-server"
     onServiceDemand="knock"
     knockMethod="fko" knockPort="62201"
     knockEncoding="Base64"
     knockSecret="1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs="
     knockSecretAuth="1k00+6JhD4LIKAqkVtLN6FCEHZxQamCbGqD+vyCmPjTzALzLLxatBB1tCYdDe4flf+xIqlwP6JpVHwggEk0jqA=="
     knockSecretAuthType="SHA256"
     IPv4="203.0.113.1">

     <Service name="SSH" port="22" />

   </RemoteHost>

If you are using an external fwknop client instead, configure it according to its own documentation. The keys must match those declared on the receiver side.

Replay protection
-----------------

The fko payload embeds the sender's current timestamp. When ``proofTime`` is set on the receiver, any knock whose timestamp deviates from the receiver's clock by more than the configured window is rejected. This prevents a captured packet from being replayed later.

Set ``proofTime`` to a duration generous enough to tolerate reasonable clock differences between sender and receiver — a few minutes is typically sufficient:

.. code:: xml

   <DynamicHostRange name="trusted-clients" knockMethod="fko" knockPort="62201"
     knockTimeout="30s"
     proofTime="3min"
     proofIP="true">

     <SharedSecret label="primary" encoding="Base64">
       <Key>1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs=</Key>
       <AuthKey type="SHA256">1k00+6JhD4LIKAqkVtLN6FCEHZxQamCbGqD+vyCmPjTzALzLLxatBB1tCYdDe4flf+xIqlwP6JpVHwggEk0jqA==</AuthKey>
     </SharedSecret>

   </DynamicHostRange>

``proofIP="true"`` additionally verifies that the source IP observed in the knock packet matches the IP embedded in the SPA payload. This works correctly for senders with a direct internet connection; for senders behind NAT the embedded IP is the private address, which will not match the public IP seen by the receiver. NAT traversal support is planned for a future release.

Symmetric encryption
--------------------

The fwknop protocol supports **128**, **192**, and **256**-bit `AES`_ encryption keys. Keys must be byte sequences of exactly the corresponding length (16, 24, or 32 bytes) and should be as random as possible.

Hash-based message authentication
----------------------------------

Although the protocol allows omitting the HMAC key, you should always include one. Without it, an attacker who captures a packet can attempt to forge a modified payload; the HMAC makes this computationally infeasible.

The digest algorithm is specified via the ``type`` attribute of ``<AuthKey>`` on the receiver, and via ``knockSecretAuthType`` on the sender. ``SHA256`` is the default and matches the fwknop reference implementation. The recommended minimum key length depends on the algorithm's internal block size:

=========== ==================== ===================== ========================
Algorithm   Hash Output Size     Internal Block Size   Recommended Key Length
=========== ==================== ===================== ========================
MD5         16 bytes (128 bit)   64 bytes              ≥ 16 bytes
SHA1        20 bytes (160 bit)   64 bytes              ≥ 20 bytes
SHA256      32 bytes (256 bit)   64 bytes              ≥ 32 bytes
SHA384      48 bytes (384 bit)   128 bytes             ≥ 48 bytes
SHA512      64 bytes (512 bit)   128 bytes             ≥ 64 bytes
SHA3-256    32 bytes             136 bytes*            ≥ 32 bytes
SHA3-512    64 bytes             72 bytes*             ≥ 64 bytes
=========== ==================== ===================== ========================

MD5 and SHA1 are effectively broken and should not be used. SHA256 or stronger is recommended.

.. admonition:: Work in progress

   Desomnia implements the core fwknop feature set. The following are not yet supported:

   - Automatic firewall reconfiguration. The SPA packet is only used to configure Desomnia's internal packet filter.
   - Clients behind NAT when ``proofIP`` is enabled on the receiver.
   - GnuPG encryption — only symmetric AES (Rijndael) is supported.
   - Encryption modes other than CBC, or key derivation other than PBKDF1.
   - Accepting knock packets over real sockets (TCP or UDP) — only passive packet capture is supported.
   - Sending or validating the local username in the wire format.
   - Running custom commands specified in the SPA packet.
   - SPA over Tor.

.. _`fwknop`: https://github.com/mrash/fwknop
.. _`AES`: https://en.wikipedia.org/wiki/Advanced_Encryption_Standard
.. _`HMAC`: https://en.wikipedia.org/wiki/HMAC
