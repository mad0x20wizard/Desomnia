Knocking
========

.. _generating-cryptographically-strong-keys:

Generating keys
---------------

In order to make the **Single Packet Authorization** as secure as possible, you have to generate unique and random keys to be used for encryption and authentification. Use the following platform provided tools, to create cryptographically strong keys:

Using OpenSSL
+++++++++++++

:OS: 🐧 *Linux* 🍎 *macOS*

The most simple way of aquiring such keys on Unix-based platforms is through the use of the ``openssl`` command:

.. code:: bash

    openssl rand -base64 32

This create a key with a length of 256 bit (32 byte) and writes it directly in Base64-encoded format to the terminal:

.. code::

    1RNh13FmfBTiMT+/VPEMVXUnRXtg+2/nbVVY+O4ENcs=

Via PowerShell
++++++++++++++

:OS: 🪟 *Windows*

On Windows you can make use of a cryptographically strong number generator via PowerShell:

.. code:: PowerShell

    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [Convert]::ToBase64String($bytes)

This also produces a suitable Base64-encoded string in your terminal.
