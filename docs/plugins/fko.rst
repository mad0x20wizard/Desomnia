Firewall Knock Operator
=======================

:OS: 🪐 *Platform-independent*

This plugin aims to become a fully compatible implementation for acting as a `fwknop`_ server and client. It would go beyond the scope of this documentation to explain all the concepts behind this technique. You can read everything you need to know on the project's website. This document will focus on highlighting notable differences in use and implementation and give practical advice on how to do Single Packet Authorization.

.. admonition:: Work in progress

    You can already use a great subset of the main features provided by fwknop. But since this software has a huge and rather complex feature set, some features are still needed to be implemented. Currently the following is **not** possible:

    - Automatic reconfiguration of the system firewall. The SPA packet is only used to configure the internal packet filter, so that Desomnia react only to a certain range of source IP addresses.
    - Support for clients behind a NAT, if ``proofIP`` is enabled on the server side.
    - Using GnuGP as encryption method, only symmetric keys via Rijndael can be used.
    - Encryption modes other than CBC or a key derivation other than PBKDF1.
    - Accepting packets over real sockets, either TCP or UDP. Only passive packet capturing is supported.
    - Sending or validating the local username in the wire format.
    - Running custom commands either configured on the server side or specified by the client inside the SPA packet.
    - Doing SPA over TOR.

Specifiying keys
----------------

To establish security, fwknop uses up to two different keys. The first one is mandatory and used to encrypt the payload of the SPA packet, so that nobody can see what you are up to. The second one is used to authenticate the message, to ensure that nobody changes anything during transit and to reduce the possibility to create forged messages dramatically.

.. attention::

    Please read on :ref:`how to generate cryptographically strong keys <generating-cryptographically-strong-keys>` for your platform.

You can use these directly to include them in your configuration file on the server:

.. code:: xml

    <ForeignHostFilterRule>
        <DynamicHostRange name="trusted-clients" knockMethod="fko" knockPort="62201">
            <SharedSecret encoding="Base64">
                <Key>1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs=</Key>
                <AuthKey type="SHA256">1k00+6JhD4LIKAqkVtLN6FCEHZxQamCbGqD+vyCmPjTzALzLLxatBB1tCYdDe4flf+xIqlwP6JpVHwggEk0jqA==</AuthKey>
            </SharedSecret>
        </DynamicHostRange>
    </ForeignHostFilterRule>

If you also use Desomnia for sending SPA packets, you need to match these keys in your client configuration:

.. code:: xml

    <RemoteHost name="example.com" onServiceDemand="knock"
      knockMethod="fko" knockPort="62201"
      knockSecret="1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs="
      knockSecretAuth="1k00+6JhD4LIKAqkVtLN6FCEHZxQamCbGqD+vyCmPjTzALzLLxatBB1tCYdDe4flf+xIqlwP6JpVHwggEk0jqA=="
      knockSecretAuthType="SHA256"
      knockEncoding="Base64">

      <Service name="SSH" port="22" />

    </RemoteHost>

Symmetric encryption key
++++++++++++++++++++++++

The implementation of fwknop supports the use of **128** / **192** / **256** bit `AES`_ encryption keys. These keys are represented by a byte sequence of the respective length and should be as cryptographically random as possible.

Hash-based message authentification
+++++++++++++++++++++++++++++++++++

Although the protocol allows you to opt out of using an `HMAC`_, you should always add a second secret key to authenticate the message. This provides an additional security layer and makes it substantially harder for attackers to tamper with or forge messages.

The protocol allows you use any digest algorithm listed for the ``type`` attribute of the ``<AuthKey>`` belonging to a shared secret. If you don't specify an algorithm explicitly, ``SHA256`` is used as a default, in accordance with the fwknop implementation.

The recommended key length for a particular digest algorithm varies and depends on their internal block size. Use this table to select an appropriate key length:

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

Since MD5 and SHA1 are effectively broken, you should consider to use at least **SHA-256** or **SHA-512**.

.. _`fwknop`: https://github.com/mrash/fwknop
.. _`AES`: https://en.wikipedia.org/wiki/Advanced_Encryption_Standard
.. _`HMAC`: https://en.wikipedia.org/wiki/HMAC