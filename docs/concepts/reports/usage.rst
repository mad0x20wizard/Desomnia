Usage monitoring
================

To get a clear picture of when your computer is awake and why, you can advise the logging system to create a dedicated log file that includes the reasons why it is not sleeping.

Example
-------

For simplicities sake we omit in this example all the rules used for debugging Desomnia. In the following code block you will find only those rules and targets, that will create a singular ``usage.log``, which will include the times when the service starts and stop, and a collections of reasons for not sleeping for every timeout interval.

The ``usage.log`` file only contains events for the current day. At the end of each day, the current log is archived and a summary containing the duration that the system was asleep is written. Only the last 10 days are preserved. You can configure these parameters yourself, changing the approriate values in the ``NLog.config``.

.. code:: xml

    <?xml version="1.0" encoding="utf-8" ?>
    <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xsi:schemaLocation="NLog NLog.xsd"
        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
        throwConfigExceptions="true"
        autoReload="true">

        <targets>
            <target xsi:type="File" name="usage" fileName="${var:logDir}\usage.log"
                    layout="${date:format=HH\:mm}     ${event-properties:item=tokens}"
                    archiveFileName="${var:logDir}\archive\usage-{###}.log" 
                    archiveNumbering="Date" 
                    archiveDateFormat="yyyy-MM-dd" 
                    archiveEvery="Day" maxArchiveDays="10"
                    footer="Total sleep duration: ${sleep-duration:archive=true}"
                    writeFooterOnArchivingOnly="True" />

            <target xsi:type="File" name="sleep" fileName="${var:logDir}\usage.log" layout="zzzZZZzzz... (${message})" />
            <target xsi:type="File" name="startup" fileName="${var:logDir}\usage.log" 
                layout="${date:format=HH\:mm}     Startup." />
            <target xsi:type="File" name="shutdown" fileName="${var:logDir}\usage.log" 
                layout="${date:format=HH\:mm}     Shutdown. Total sleep duration: ${sleep-duration}${newline}" />
        </targets>

        <rules>
            <logger name="MadWizard.Desomnia.SystemUsageInspector" level="Info" writeTo="usage" final="true" />
            <logger name="MadWizard.Desomnia.Power.Watch.SleepWatch" level="Info" writeTo="sleep" final="true" />
            <logger name="MadWizard.Desomnia.Power.Watch.StartupWatch" level="Info" writeTo="startup" final="true" />
            <logger name="MadWizard.Desomnia.Power.Watch.ShutdownWatch" level="Info" writeTo="shutdown" final="true" />
        </rules>
    </nlog>