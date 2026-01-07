using MadWizard.Desomnia.Pipe.Config;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesomniaSessionMinion.Services.NotificationArea
{
    internal static class SessionHelper
    {
        public static bool IsDuoInstance(this SessionInfo sessionInfo)
        {
            if (OpenDuoInstancesKey() is RegistryKey instances)
                try
                {
                    foreach (var name in instances.GetSubKeyNames())
                    {
                        RegistryKey instanceKey = instances.OpenSubKey(name);

                        try
                        {
                            var sessionID = (int?)instanceKey.GetValue("SessionId");

                            if (sessionID == sessionInfo.Id)
                                return true;
                        }
                        finally
                        {
                            instanceKey.Dispose();
                        }

                    }
                }
                finally
                {
                    instances.Dispose();
                }

            return false;
        }

        private static RegistryKey OpenDuoInstancesKey()
        {
            RegistryKey duo = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Duo");

            if (duo != null)
            {
                RegistryKey instances = duo.OpenSubKey("Instances"); // since Duo 1.5.0

                if (instances != null)
                {
                    duo.Dispose();

                    return instances;
                }
                else
                    return duo;
            }
            return null;
        }

    }
}
