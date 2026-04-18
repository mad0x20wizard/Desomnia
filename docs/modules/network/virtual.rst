Virtual machines
================

Desomnia can monitor virtual machines as individual network resources, independently of their physical host. This requires the VM to use a **bridged network interface** — not NAT. With a bridged interface, the VM appears on the physical network with its own MAC address, which is what Desomnia needs to identify and watch it. With NAT, all VM traffic is indistinguishable from the physical host's traffic and the VM is invisible to Desomnia.

When this becomes useful
------------------------

If you run VMs in your network that offer services reachable under their own host names, you can make the NetworkMonitor aware of them:

.. code:: xml

    <NetworkMonitor interface="eth0">

        <LocalHost>
            <VirtualHost name="WINDOWS-TEST" MAC="00:15:5D:80:5C:04" onDemand="start" onIdle="suspend">
                <Service name="RDP" port="3389" />
            </VirtualHost>
        </LocalHost>

        <RemoteHost name="boss" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.178.100">

            <Service name="RDP" port="3389" />
            <Service name="SMB" port="445" />

            <VirtualHost name="dev" MAC="00:15:5D:3B:01:00">
                <Service name="SSH" port="22" />
            </VirtualHost>

            <VirtualHost name="gitlab" MAC="00:15:5D:3B:01:05">
                <Service name="SSH" port="22" />
                <Service name="HTTP" port="80" />
            </VirtualHost>

            <VirtualHost name="nextcloud" MAC="00:15:5D:80:5C:00">
                <Service name="HTTP" port="8080" />
            </VirtualHost>

        </RemoteHost>

    </NetworkMonitor>

Local vs. remote
----------------

There are two distinct configurations, depending on whether the VM runs on the local machine or on a remote host.

``<LocalHost>`` × ``<VirtualHost>``
    Desomnia can automatically start a local VM when one of its services is accessed, and suspend or stop it once it becomes idle. The ``onDemand`` and ``onIdle`` events on a local ``<VirtualHost>`` trigger actions handled by a hypervisor plugin.

    As long as one of the VM's services is being accessed regularly, the VM also prevents the physical host from going to sleep.

    The following hypervisor plugins are currently available:

    - :doc:`Hyper-V </plugins/hyperv>` (🪟 Windows)

``<RemoteHost>`` × ``<VirtualHost>``
    VMs on a remote host cannot be controlled directly by Desomnia. When a connection attempt to one of their services is detected, Desomnia wakes the physical host with a Magic Packet and leaves it to the remote host to bring the VM online.

    If Desomnia is also running on the remote host as a :doc:`local resource manager </guides/sleep>`, it can handle the second step automatically — starting the VM once the physical host is back online.
