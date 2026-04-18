Actions and Events
==================

The monitoring architecture is complemented by a flexible event and action system that enables automated responses to changes in resource state. This mechanism allows the system to react dynamically to both idle conditions and activity triggers across the resource hierarchy.

Event-Driven Behavior
---------------------

Resources and resource monitors can emit events based on state transitions or specific conditions. These events can be configured to trigger predefined actions, allowing users to define custom automation behavior.

The most commonly used events are:

- ``onIdle``
- ``onDemand``

Not all resources support every event type. The availability of events depends on the specific resource or monitor implementation and is documented individually.

The ``onIdle`` Event
-------------------

The ``onIdle`` event is triggered when a resource or resource monitor is detected as idle during a timeout evaluation phase.

For many (but not all) resources, users can configure an action that will be executed when this event occurs. These actions are defined per resource type and documented alongside the respective monitor.

Delayed Execution
^^^^^^^^^^^^^^^^^

The execution of an ``onIdle`` action can be delayed using a flexible configuration syntax:

Time-based delay::

    onIdle="action+5min"

The action is executed after the resource has remained idle for the specified duration.

Cycle-based delay::

    onIdle="action+2x"

The action is executed only after the resource has been reported idle for a specified number of consecutive timeout evaluation cycles.

If the resource becomes active again before the delay condition is fulfilled, the pending action is **cancelled**. This ensures that actions are only executed when the idle condition is stable and sustained.

The ``onDemand`` Event
---------------------

The ``onDemand`` event is triggered when a resource transitions from an idle state to an active state due to an external stimulus.

This is particularly relevant for resources that can be activated externally, such as network services or user-driven inputs. In such cases, the ``onDemand`` event can be used to initiate automated actions, such as:

- Starting dependent services
- Initializing required components
- Preparing the system for incoming activity

Not all resources support ``onDemand``, as it requires the ability to detect explicit activation triggers.

Additional Resource-Specific Events
----------------------------------

Beyond the standard events, some resources define their own specialized events tailored to their domain. For example:

- ``onLogin`` for user session monitors
- Other context-specific lifecycle or state-change events

These events provide additional integration points for automation and can be configured similarly to ``onIdle`` and ``onDemand``.

Because these events are resource-specific, their availability and semantics must be obtained from the documentation of the corresponding resource monitor.

Hierarchical Action Resolution
-----------------------------

Actions are organized in a hierarchical manner, mirroring the structure of the resource tree.

When an event is triggered on a given node:

- The node can access and execute its **own actions**
- It can also reference and utilize **actions defined in its parent nodes**

This hierarchical model enables:

- Reuse of common actions across multiple resources
- Centralized definition of shared behavior
- Flexible composition of actions at different levels of the tree

As a result, even deeply nested resources can leverage higher-level logic, either to use global actions (like ``exec``) or to short-circuit the idle detection pipeline for specific use cases.

Summary
-------

The event and action system provides a powerful automation layer built on top of the monitoring architecture:

- Events are triggered by **state changes** or **resource-specific conditions**
- ``onIdle`` enables actions based on **sustained inactivity**, with optional delays
- ``onDemand`` allows reactions to **external activation triggers**
- Additional events extend functionality for **specialized resource types**
- Actions are resolved **hierarchically**, promoting reuse and modularity

This design allows users to define precise, context-aware automation workflows while maintaining a clear and structured configuration model.