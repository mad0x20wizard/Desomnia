Promiscuous mode
================

Normal Mode
-----------

In **normal mode**, the application monitors **outgoing network traffic** to detect interactions with remote hosts.

This mode focuses on two primary aspects:

- **Reachability detection**  
    When a remote host is accessed but does not respond, the application assumes that the host may be in a suspended state. In this case, it can automatically wake the host using a *Wake-on-LAN* magic packet.

- **Activity tracking**  
    Network traffic directed to a remote host is analyzed and counted to determine whether the connection is actively in use.

Each monitored remote host is represented as a **resource** within the system. If a remote host is considered active (i.e., not idle), it contributes to the overall system state.

If *local resource monitoring* is enabled, active connections to (configured) remote hosts will prevent the local system from transitioning into an idle state or entering sleep mode.

Promiscuous Mode (Wake Proxy)
-----------------------------

In **promiscuous mode** the application performs deeper inspection of network traffic by monitoring both **outgoing and incoming packets**.

This mode extends the functionality of normal mode with the following capabilities:

- **Incoming traffic analysis**  
  The application listens for specific multicast or broadcast messages that indicate connection attempts between remote hosts.

- **Connection intent detection**  
  By analyzing these packets, the system can determine when one remote host is attempting to establish a connection to another.

- **Traffic interception and redirection**  
  If the target host is currently suspended, the application can temporarily redirect relevant traffic to the local network interface. This allows for additional inspection, such as validating the destination port or protocol.

- **Selective wake-up**  
  Based on this deeper inspection, the application decides whether waking the target host is necessary. This helps avoid unnecessary wake-ups that would otherwise occur with simpler detection mechanisms.

Operational Considerations
-------------------------

Promiscuous mode introduces specific operational requirements:

- The system running in this mode should ideally remain **continuously online**, as it acts as an intermediary for network communication.
- It is **not recommended** to use this mode in combination with local system sleep control, as suspending the proxy would disrupt its functionality.
- The preferred deployment target is a **low-power device**, such as a small always-on system (e.g., a home server or similar device).

Security Considerations
----------------------

Promiscuous mode modifies normal network behavior by inspecting and conditionally redirecting traffic. This can have implications in managed or security-sensitive environments.

Therefore:

- This mode is **not suitable for heterogeneous or centrally managed networks**, such as corporate environments.
- It may trigger **security monitoring systems**, intrusion detection mechanisms, or policy violations.
- Its use is recommended **only within controlled environments**, such as private home networks or small office setups under a single administrative domain.
