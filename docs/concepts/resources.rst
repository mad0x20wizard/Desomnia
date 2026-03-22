Resources and Usage
===================

The application is built around a hierarchical monitoring model that represents system activity as a tree of interconnected components. This model enables flexible, extensible, and fine-grained detection of system usage and idleness.

At the root of this hierarchy is the SystemMonitor, which represents the overall state of the system. Its primary responsibility is to determine whether the system is currently idle or in use. Rather than relying on a single metric, it derives this state by evaluating a structured tree of subordinate monitoring components.

Hierarchical Monitoring Model
-----------------------------

The monitoring model consists of two fundamental elements:

- Resource Monitors
    These are composite nodes that can contain other resource monitors or resources. They implement logic to aggregate the state of their children and determine their own activity state.

- Resources
    These are the leaf nodes of the tree. Each resource represents a concrete activity source (e.g., user input, network activity, application state) and can directly report whether it is idle or active.

This structure forms a recursive tree::

    SystemMonitor
    └── ResourceMonitor
        ├── Resource
        ├── ResourceMonitor
        │   └── Resource
        └── Resource

Each **ResourceMonitor** evaluates its children and reports its state upward. This allows complex activity detection logic to be composed from simpler building blocks.

Idle Detection Mechanism
------------------------

The system evaluates idleness based on a configurable timeout interval defined by the user. After this timeout elapses, the following process is triggered:

1. All resources and resource monitors in the tree are queried for their current state.
2. Each node determines whether it is idle or active:
 - A **Resource** directly reports its state.
 - A **ResourceMonitor** aggregates the state of its children.
3. The **SystemMonitor** evaluates its immediate children and derives the global system state.

Partial Idleness and Granularity
--------------------------------

An important characteristic of this architecture is that idleness is not strictly binary across the entire system. While the **SystemMonitor** produces a global state, individual branches of the resource tree may still be idle even when others are active.

This partial idleness is a key design feature. It enables:

- Fine-grained visibility into which parts of the system are active
- Selective triggering of actions based on specific subtrees
- Flexible extension of monitoring logic without affecting the entire system

This concept becomes particularly relevant in later sections, where actions can be bound to specific parts of the resource hierarchy rather than the system as a whole.
