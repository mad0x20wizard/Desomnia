using Autofac;
using DesomniaSessionMinion.Properties;
using DesomniaSessionMinion.Services.NotificationArea;
using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Config;
using MadWizard.Desomnia.Pipe.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Mime;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Timers.Timer;
using System.Windows.Input;

namespace MadWizard.Desomnia.Minion
{
    class NotificationAreaController : IStartable, IDisposable
    {
        internal ILogger<NotificationAreaController> _logger;
        internal UserInterfaceContext _userInterface;
        internal PipeMessageBroker _pipeBroker;

        NotifyIcon _notifyIcon;

        SleeplessConfigurationWindow _window;

        NotificationAreaConfig Config { get; set; }
        InspectionMessage Inspection { get; set; }

        private Timer _timerSleepless;

        public NotificationAreaController(
            ILogger<NotificationAreaController> logger, 
            PipeMessageBroker broker, 
            UserInterfaceContext ui)
        {
            this._logger = logger;
            this._pipeBroker = broker;
            this._userInterface = ui;

            broker.RegisterMessageHandler<NotificationAreaMessage>(HandleUpdate);
            broker.RegisterMessageHandler<InspectionMessage>(HandleInspection);
        }

        uint SessionId => (uint)Process.GetCurrentProcess().SessionId;
        Size IconSize => new Size((int)(16 * _userInterface.Scaling), (int)(16 * _userInterface.Scaling));

        void IStartable.Start()
        {
            // TODO remove me
        }

        private void HandleUpdate(NotificationAreaMessage message)
        {
            Config = message.Config;

            _userInterface.SendAction(UpdateNotifyIcon);
            _userInterface.SendAction(UpdateConfigurationDialog);
        }

        private void HandleInspection(InspectionMessage message)
        {
            Inspection = message;

            _userInterface.SendAction(UpdateConfigurationDialog);
        }

        private void UpdateConfigurationDialog()
        {
            if (_window != null)
            {
                _window.ShouldBeSleeplessUntil = Config.SleeplessUntil;
                _window.ShouldBeSleeplessIfUsage = Config.SleeplessIfUsage;

                if (Inspection != null)
                {
                    _window.Tokens = Inspection.Tokens;
                    _window.LastInspection = Inspection.Time;
                    _window.NextInspection = Inspection.NextTime;
                }
            }
        }

        private void ConfigurationWindow_InspectionRequested(object sender, EventArgs e)
        {
            _pipeBroker.SendMessage(new RequestInspectionMessage { });
        }

        private void ConfigurationWindow_SleeplessChanged(object sender, EventArgs args)
        {
            _pipeBroker.SendMessage(new SleeplessMessage()
            {
                SleeplessUntil = _window.ShouldBeSleeplessUntil,
                SleeplessIfUsage = _window.ShouldBeSleeplessIfUsage
            });
        }

        private void ConfigurationWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            _window.SleeplessChanged -= ConfigurationWindow_SleeplessChanged;
            _window.InspectionRequested -= ConfigurationWindow_InspectionRequested;
            _window.FormClosed -= ConfigurationWindow_FormClosed;
            _window.Dispose();
            _window = null;
        }

        private void UpdateNotifyIcon()
        {
            if (_notifyIcon == null)
            {
                _notifyIcon = new NotifyIcon
                {
                    Text = "Desomnia",
                    Icon = Resources.Moon,
                    Visible = true
                };

                _notifyIcon.Click += NotifyIcon_Click;
                _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
            }

            _notifyIcon.Icon = Config.SleeplessUntil != null ? Resources.Sleepless : Resources.Moon;

            _notifyIcon.ContextMenu?.Dispose();
            _notifyIcon.ContextMenu = CreateContextMenu();
        }

        private void NotifyIcon_Click(object sender, EventArgs args)
        {
            if (Config.MayConfigureSleepless)
            {
                bool ignore = (args as System.Windows.Forms.MouseEventArgs)?.Button == MouseButtons.Right;

                if (!ignore)
                {
                    _timerSleepless = new Timer(500);
                    _timerSleepless.Elapsed += (s, a) => _pipeBroker.SendMessage(new SleeplessMessage(!Config.Sleepless));
                    _timerSleepless.AutoReset = false;
                    _timerSleepless.Start();
                }
            }
        }
        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            if (Config.MaySuspendSystem)
            {
                _timerSleepless?.Stop();

                _pipeBroker.SendMessage(new EnterStandbyMessage());
            }
        }

        private ContextMenu CreateContextMenu()
        {
            var context = new ContextMenu();

            context.Popup += (sender, args) =>
            {
                context.MenuItems.Clear();

                context.MenuItems.AddRange(CreateSessionItems().ToArray());

                if (Config.Sessions[SessionId].IsRemoteConnected)
                {
                    if (context.MenuItems.Count > 0)
                        context.MenuItems.Add("-");
                    context.MenuItems.Add(CreateRemoteSessionsItem());
                }

                if (Config.MayConfigureSleepless || Config.MaySuspendSystem)
                    if (context.MenuItems.Count > 0)
                        context.MenuItems.Add("-");

                if (Config.MayConfigureSleepless)
                    context.MenuItems.Add(CreateSleeplessItem());
                if (Config.MaySuspendSystem)
                    context.MenuItems.Add(CreateSleepItem());
            };

            return context.WithMenuItemImages();
        }

        private IEnumerable<MenuItem> CreateSessionItems()
        {
            if (!Config.Sessions.Values.Aggregate(false, (may, session) => 
                may || session.MayConnectToConsole || session.MayDisconnect))
                yield break;

            yield return new ImageMenuItem($"Konsolen-Sitzung", Resources.DisplayConsole.ResizeTo(IconSize)) { Enabled = false };
            
            yield return new MenuItem("-");

            int sessionNr = 1;
            bool mayDisconnectConsole = false;
            foreach (var session in Config.Sessions.Values)
            {
                Bitmap icon = null;
                if (session.IsRemoteConnected)
                    if (session.IsDuoInstance())
                        icon = Resources.DuoInverse;
                    else
                        icon = Resources.RDP;

                MenuItem item = icon != null
                    ? new ImageMenuItem(session.Name, icon.ResizeTo(IconSize))
                    : new MenuItem(session.Name);

                item.Enabled = session.MayConnectToConsole && !session.IsDuoInstance();
                item.Checked = session.IsConsoleConnected;
                item.DefaultItem = session.Id == SessionId;

                #region Shortcuts
                if (sessionNr <= 12 && item.Enabled)
                {
                    string fx = "F" + sessionNr++;
                    var fKey = Keys.F1;

                    Keys.TryParse(fx, out fKey);

                    item.UpdateShortcut(Keys.Control, Keys.Alt, fKey);

                    if (!GlobalHotKey.RegisterHotKey("Control + Alt + " + fx, () => _pipeBroker.SendMessage(new ConnectToConsoleMessage(session.Id))))
                        _logger.LogWarning("HotHey Registration failed: Control + Alt + " + fx);
                }
                #endregion

                if (session.IsConsoleConnected)
                {
                    if (session.MayDisconnect)
                        mayDisconnectConsole = true;
                    //item.Enabled = false;
                }
                else
                {
                    if (session.MayConnectToConsole)
                        item.Click += (sender, args) => _pipeBroker.SendMessage(new ConnectToConsoleMessage(session.Id));
                    else
                        item.Enabled = false;
                }

                yield return item;
            }

            if (mayDisconnectConsole)
            {
                MenuItem itemDisconnect = new MenuItem("Trennen");

                itemDisconnect.UpdateShortcut(Keys.Control, Keys.Alt, Keys.Back);

                itemDisconnect.Click += (sender, args) => _pipeBroker.SendMessage(new ConnectToConsoleMessage(null));

                GlobalHotKey.RegisterHotKey(ModifierKeys.Control | ModifierKeys.Alt, Key.Back, () => _pipeBroker.SendMessage(new ConnectToConsoleMessage(null)));

                yield return itemDisconnect;
            }
        }

        private MenuItem CreateRemoteSessionsItem()
        {
            var item = new ImageMenuItem("Remote-Sitzung", Resources.DisplayRemote.ResizeTo(IconSize));

            if (Config.Sessions[SessionId].MayDisconnect)
            {
                var itemDisconnect = new MenuItem("Trennen");

                itemDisconnect.Click += (sender, args) => _pipeBroker.SendMessage(new DisconnectMessage(SessionId));

                item.MenuItems.Add(itemDisconnect);
            }
            else
            {
                item.Enabled = false;
            }

            return item;
        }

        private MenuItem CreateSleepItem()
        {
            var item = new ImageMenuItem("Jetzt schlafen", Resources.Sleep.ResizeTo(IconSize));
            item.Click += (sender, args) => _pipeBroker.SendMessage(new EnterStandbyMessage());
            return item;
        }
        private MenuItem CreateSleeplessItem()
        {
            var item = new MenuItem("Schlaflos") { Checked = Config.Sleepless };

            var itemWhenInUse = new MenuItem(Config.SleeplessIfUsage == true ? "Bei Benutzung" : "Nie")
            {
                RadioCheck = true,
                Checked = !Config.Sleepless,
                Shortcut = Shortcut.Del,
            };

            itemWhenInUse.Click += (sender, args) => _pipeBroker.SendMessage(new SleeplessMessage(false));

            item.MenuItems.Add(itemWhenInUse);

            item.MenuItems.Add("-");

            var lastDuration = TimeSpan.Zero;
            var leftDuration = Config.SleeplessUntil - DateTime.Now;

            MenuItem CreateSleeplessDurationItem(string name, TimeSpan duration)
            {
                var itemDur = new MenuItem(name)
                {
                    RadioCheck = true,
                    Checked = leftDuration > lastDuration && leftDuration < duration
                };

                itemDur.Click += (sender, args) => _pipeBroker.SendMessage(new SleeplessMessage(duration));

                lastDuration = duration;

                return itemDur;
            }

            item.MenuItems.Add(CreateSleeplessDurationItem("30 Minuten",    TimeSpan.FromMinutes(30)));
            item.MenuItems.Add(CreateSleeplessDurationItem("1 Stunde",      TimeSpan.FromHours(1)));
            item.MenuItems.Add(CreateSleeplessDurationItem("2 Stunden",     TimeSpan.FromHours(2)));
            item.MenuItems.Add(CreateSleeplessDurationItem("4 Stunden",     TimeSpan.FromHours(4)));

            MenuItem itemConfigure;
            if (leftDuration > lastDuration && Config.SleeplessUntil < DateTime.MaxValue)
            {
                var until = DateTime.Now.Date == Config.SleeplessUntil.Value.Date 
                    ? Config.SleeplessUntil.Value.ToString("t") 
                    : Config.SleeplessUntil.ToString();

                itemConfigure = new ImageMenuItem($"Aktiv bis {until} ...", Resources.Clock.ResizeTo(IconSize))
                {
                    RadioCheck = true,
                    Checked = true
                };

                item.MenuItems.Add(itemConfigure);
            }
            else
            {
                itemConfigure = new ImageMenuItem("Benutzerdefiniert...", Resources.Clock.ResizeTo(IconSize));

                item.MenuItems.Add(itemConfigure);
            }

            itemConfigure.Click += (sender, args) =>
            {
                if (_window == null)
                {
                    _window = new SleeplessConfigurationWindow();
                    _window.SleeplessChanged += ConfigurationWindow_SleeplessChanged;
                    _window.InspectionRequested += ConfigurationWindow_InspectionRequested;
                    _window.FormClosed += ConfigurationWindow_FormClosed;
                }

                UpdateConfigurationDialog();

                _window.Visible = true;
                _window.BringToFront();
            };

            item.MenuItems.Add("-");

            var itemPermanent = new ImageMenuItem("Dauerhaft", Resources.Infinity.ResizeTo(IconSize))
            {
                RadioCheck = true,
                Checked = Config.SleeplessUntil == DateTime.MaxValue,
                Shortcut = Shortcut.ShiftDel,
            };

            itemPermanent.Click += (sender, args) => _pipeBroker.SendMessage(new SleeplessMessage(true));

            item.MenuItems.Add(itemPermanent);

            return item;
        }

        private void DestroyTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }

        void IDisposable.Dispose()
        {
            _userInterface.SendAction(DestroyTrayIcon);
        }
    }
}
