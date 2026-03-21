Virtual machines
================

Because this is a somehow advanced feature, the ramifications of having virtual machines in your setup will be discussed here separately.

When this becomes useful
------------------------

If you run a couple of VMs in your network, which offer dedicated services that are reachable under individual host names and share a bridged network interface with their respective physical host, it can be beneficial to make the Network Monitor aware of them:

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
                <Service name="SSH" protocol="TCP" port="22" />
            </VirtualHost>

            <VirtualHost name="gitlab" MAC="00:15:5D:3B:01:05">
                <Service name="SSH" port="22" />

                <HTTPService port="80">
                    <RequestFilterRule path="users/sign_in" />

                    <RequestFilterRule type="Must">
                        <Cookie name="visitor_id" />
                    </RequestFilterRule>
                </HTTPService>
            </VirtualHost>
            
            <VirtualHost name="nextcloud" MAC="00:15:5D:80:5C:00">
                <Service name="HTTP" port="8080" />
            </VirtualHost>
    
        </RemoteHost>
        
    </NetworkMonitor>

Local vs. remote
----------------

There are two different types of virtual hosts, identified by the combination of parent and child node:

``<LocalHost>`` × ``<VirtualHost>``
    You can configure Desomnia to automatically start local virtual machines when one of their services is accessed by a remote host. You can also opt to suspend or stop them when they eventually become idle.

    As long as one of their services is accessed over the network regularly, local virtual machines will also prevent the physical host from going to sleep.

``<RemoteHost>`` × ``<VirtualHost>``
    Since virtual machines on remote hosts cannot be controlled directly by any standard protocol, your options are a bit more limited here. So when Desomnia registers a connection attempt to one of their services, it will at least try to wake up their physical host.
    
    If it happens, that you also run Desomnia as a :doc:`local resource manager </guides/sleep>` on the remote host, it can take it over from there and start the virtual machine if it is not already running.