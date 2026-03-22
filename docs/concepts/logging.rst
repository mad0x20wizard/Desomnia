Logging
=======

If you do not provide a ``NLog.config`` file, Desomnia will only write **INFO** events to the console output. Once you have created a configuration file, you can set up detailed logging and write it to the specified files.

Example
-------

This is a example configuration, that is used for debugging Desomnia. You can either have a trace.log to see every event in one single file or a dedicated log file for each host, so that it is easier to follow the stream of events. You can also have both, if you like.

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

            <target xsi:type="File" name="network"	fileName="${var:logDir}/network/${scopeproperty:item=HostName:whenEmpty=network}.log" layout="${var:sharedLayout}" />
            <target xsi:type="File" name="network:trace" fileName="${var:logDir}/network/trace/${scopeproperty:item=HostName:whenEmpty=trace}.log" layout="${var:sharedLayout}" />

            <target xsi:type="Console" name="console" layout="${pad:padding=5:inner=${level:uppercase=true}} :: ${message} ${exception}" />
        </targets>

        <rules>
            <logger name="Program" minlevel="Info" writeTo="console" />

            <logger name="MadWizard.Desomnia.Network.*" minlevel="Info" writeTo="console" />

            <logger name="MadWizard.Desomnia.Network.Trace.*" minlevel="Trace" writeTo="network:trace" final="true" />
            <logger name="MadWizard.Desomnia.Network.*" minlevel="Trace" writeTo="network" final="true" />

            <logger name="MadWizard.Desomnia.*" minlevel="Debug" writeTo="desomnia" />
        </rules>
    </nlog>

Variables
---------

If you do not explicitly declare the variables as shown in the example above, Desomnia will provide default values for them:

logDir
    The path inside this variable should be the base directory of all your log files. If you run Desomnia in portable mode, a subdirectory ``logs`` will be created in the current working directory. In all other cases, a designated log directory will be used.

sharedLayout
    This variable can be used to ensure a consistent layout across all your log files. The default value is:

    .. code::

        ${longdate} ${pad:padding=5:inner=${level:uppercase=true}} ${logger:shortName=true} :: ${message} ${exception}

Loggers
-------

Here is a description of common logger namespaces:

Program
    Startup and configuration errors.

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

