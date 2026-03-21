/etc/desomnia
    Put a suitable ``monitor.xml`` into this location, to tell Desomnia what to do. You can also create a ``NLog.config`` file here, to configure additional :doc:`logging </concepts/logging>`, beside the console output.

/var/lib/desomnia/plugins
    Here you can drop your additional plugins, which will be loaded when the programs starts.

/var/log/desomnia
    Here you will find the log files, if you enabled file :doc:`logging </concepts/logging>` in the ``NLog.config`` and used ``${var:logDir}`` as base path. 

