using MadWizard.Desomnia.Minion;
using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Config;
using MadWizard.Desomnia.Pipe.Messages;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Minion
{
    public class LastInputService : BackgroundService
    {
        private MinionConfig _config;

        private PipeMessageBroker _broker;

        public LastInputService(MinionConfig config, PipeMessageBroker broker)
        {
            _config = config;
            _broker = broker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_config.PushInterval.HasValue)
            {
                var interval = (int)_config.PushInterval.Value.TotalMilliseconds / 2;

                while (!stoppingToken.IsCancellationRequested)
                {
                    _broker.SendMessage(new InputTimeMessage(LastInputTime));

                    await Task.Delay(interval, stoppingToken);
                }
            }
        }

        public DateTime LastInputTime
        {
            get
            {
                LASTINPUTINFO info = new LASTINPUTINFO() { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

                if (!GetLastInputInfo(ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        private struct LASTINPUTINFO
        {
            public uint cbSize;

            public uint dwTime;
        }
    }
}
