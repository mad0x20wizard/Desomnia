Logging
=======

If you do not provide a ``NLog.config`` file, Desomnia will only write **INFO**-level events in the console output. This is useful for seeing when specific actions are taken, such as waking a remote host. Once you have created a log configuration file, you will be able to see more about how things work internally.

Example
-------

This is an example configuration for debugging purposes. It splits events from different monitors into separate log files to make the stream of events easier to follow.

.. code:: xml

    <?xml version="1.0" encoding="utf-8" ?>
    <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xsi:schemaLocation="NLog NLog.xsd"
        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
        throwConfigExceptions="true"
        autoReload="true">

        <variable name="logDir" value="${currentdir:dir=logs}" />

        <variable name="sharedLayout">
            <layout xsi:type="SimpleLayout" text="${longdate} ${pad:padding=5:inner=${level:uppercase=true}} ${logger:shortName=true} :: ${message} ${exception}" />
        </variable>

        <targets>
            <target xsi:type="File" name="desomnia" fileName="${var:logDir}/desomnia.log" layout="${var:sharedLayout}" />

            <target xsi:type="File" name="process"	fileName="${var:logDir}/process.log" layout="${var:sharedLayout}" />

            <target xsi:type="File" name="network"	fileName="${var:logDir}/${network}/${host:withSource=true:whenEmpty=network}.log" layout="${var:sharedLayout}" />
            <target xsi:type="File" name="network:trace" fileName="${var:logDir}/${network}/trace/${host:whenEmpty=trace}.log" layout="${var:sharedLayout}" />

            <target xsi:type="Console" name="console" layout="${pad:padding=5:inner=${level:uppercase=true}} :: ${message} ${exception}" />
        </targets>

        <rules>
            <logger name="Program" minlevel="Info" writeTo="console" />

            <logger name="MadWizard.Desomnia.Process.*" writeTo="process" finalMinLevel="Debug" />

            <logger name="MadWizard.Desomnia.Network.*" minlevel="Info" writeTo="console" />

            <logger name="MadWizard.Desomnia.Network.Trace.*" minlevel="Trace" writeTo="network:trace" final="true" />
            <logger name="MadWizard.Desomnia.Network.*" minlevel="Trace" writeTo="network" final="true" />

            <logger name="MadWizard.Desomnia.*" minlevel="Debug" writeTo="desomnia" />
        </rules>
    </nlog>

The ``<logger>`` rules define the namespaces and classes from which events should be captured and to which target (usually a log file) they should be written. The ``final`` attribute prevents downstream loggers from capturing the same event again, which can be used to create a clean separation of concerns.

If you use this configuration as it is and depending on the enabled monitors, you will see a separate file created for process-related events and a folder for each monitored network interface. Everything else, if any, goes to a single catch-all file.

Variables
---------

If you do not explicitly declare the variables as shown in the example above, Desomnia will provide default values for them:

logDir
    The path inside this variable should be the base directory of all your log files. If you run Desomnia in portable mode, a subdirectory ``logs`` will be created in the current working directory. In all other cases, a designated log directory will be used.

sharedLayout
    This variable can be used to ensure a consistent layout across all your log files. The default value is:

    .. code::

        ${longdate} ${pad:padding=5:inner=${level:uppercase=true}} ${logger:shortName=true} :: ${message} ${exception}

Layout Renderers
----------------

Due to the nature of network traffic, events processed by the :doc:`/modules/network/monitor` do have a level of concurrency, which may render the log output incomprehensible. Therefore you can use these *Layout Renderers* to create multiple log files, based on the context of the log event:

``${network}``
    This will output the name of the NetworkMonitor configuration used to produce the output. If you did not configure a name explicitly, the name of the interface will be used (e.g. "eth0" for a wired network interface on Linux).
``${host:withSource=true|false:withRequest=true|false}``
    This will output the name of the contextual NetworkHost, while producing the output. If the event happened outside the scope of a single host, the output will be empty. Use ``:whenEmpty="network"`` to route these events into a single log file, called "network". There are two optional parameters, which values default to ``false``:
    
    - Using ``withSource=true`` will append the source host (either IP or hostname) to the filename, so that each source will have its own logfile in a directory with the name of the target host.
    - Using ``withRequest=true`` will append the number of the demand request to the filename, so that each request will have it's own logfile in a directory with the name of the target host, source host or both. Only use this, if you expect a high level of concurrency, since this will create many individual files.


Loggers
-------

Here is a description of common logger namespaces:

Program
    Startup and configuration errors.

MadWizard.Desomnia.Process.*
    Everything related to the :doc:`/modules/process/monitor`. 
    
    .. attention::
        
        If you configure ``finalMinLevel="Trace"`` while the monitor is enabled, it will log every process start / stop in the system, which can be quite a lot.
MadWizard.Desomnia.Network.*
    Everything related to the :doc:`/modules/network/monitor`
MadWizard.Desomnia.Network.Trace.*
    If enabled, you can trace individual network hosts to log incoming and outgoing network packets.

MadWizard.Desomnia.*
    Everything else, which has no need to be logged separately.


Console target
--------------

If you don't provide a ``<target xsi:type="Console" ... />``, it will automatically be complemented to your configuration with the following layout::

    ${pad:padding=5:inner=${level:uppercase=true}} :: ${message} ${exception}

This is to make sure, that the ``NLog.config`` file feels truly optional and that you don't accidentally forget to add it, while creating your custom logging configuration.

Hot reloading
-------------

When ``autoReload="true"`` is set, you can change the configuration while Desomnia is running and the logging will be changed without the need to restart the application.

Reports
-------

With the NLog logging system you can write complex rules and targets, that allow to generate diverse reports:

.. toctree::
   :maxdepth: 1

   reports/usage
