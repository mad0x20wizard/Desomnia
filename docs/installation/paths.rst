/etc/desomnia
    In this location you can put the ``monitor.xml``, which will tell Desomnia what to do. You can also create a ``NLog.config`` file here, to configure additional logging, beside the console output.

/var/lib/desomnia/plugins
    Here you can drop your custom plugins, which will be loaded when the programs starts.

/var/log/desomnia
    Here you will find the log files, if you enabled file :doc:`logging </concepts/logging>` in the NLog.config and used ``${var:logDir}`` as base path. 

