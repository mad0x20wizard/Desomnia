Environments
============

Environments apply configuration settings according to various conditions that can change their state over time. Several environments can apply simultaneously; their settings are merged into one system configuration.

In order to use environments, replace the outermost ``<SystemMonitor>`` element with ``<EnvironmentMonitor version="2">``.
``<DefaultEnvironment>`` contains shared settings, while ``<Environment>``
elements contain conditional settings.

Condition changes are detected automatically, independently of file reloading.
The ``debounce`` attribute controls the delay before a changed condition is
applied. File edits require automatic reload or a service restart.

.. toctree::
   :maxdepth: 2

   environments/config
   environments/conditions
   environments/merging
