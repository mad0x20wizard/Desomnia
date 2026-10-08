Environment Variables
=====================

Environment-variable conditions compare a variable from a selected source with
a configured value. An XML namespace identifies the source.

env:NAME
--------

``NAME`` is the environment variable name. ``env`` is a namespace prefix,
declared with ``xmlns:env`` on the element or an enclosing element:

.. code:: xml

   <EnvironmentMonitor version="3" xmlns:env="environment:process">
     <Environment name="Lab" env:DESOMNIA_LOCATION="lab">
       <SystemMonitor timeout="5min">
         <PowerRequestMonitor />
       </SystemMonitor>
     </Environment>
   </EnvironmentMonitor>

An XML namespace qualifies an attribute name so that it can be distinguished
from attributes with the same local name. Here, ``env:DESOMNIA_LOCATION`` refers
to a variable condition rather than a base configuration attribute. For example,
``env:name`` would test a variable called ``name``; the unprefixed ``name``
attribute names the environment itself.

The prefix is arbitrary: ``xmlns:vars="environment:process"`` with
``vars:DESOMNIA_LOCATION="lab"`` has the same meaning. The namespace identifier
``environment:process`` determines the source; it is not a file path or an
address that Desomnia retrieves.

Values are matched exactly, including case. An empty configured value matches
an unset or empty variable.

.. rubric:: Variable sources

The namespace identifies the environment-variable source. Other sources are
possible, but only the following source is currently implemented:

``environment:process``
    Uses the environment variables of the Desomnia process. For a service,
    these are the variables available to the service when it starts, which may
    differ from those in an interactive terminal.

    These variables are not refreshed while Desomnia is running. Changes to
    the service's environment require a process restart; reloading the XML
    configuration does not update them.
