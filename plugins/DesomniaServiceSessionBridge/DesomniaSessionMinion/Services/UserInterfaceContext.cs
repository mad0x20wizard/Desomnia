using Autofac;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MadWizard.Desomnia.Minion
{
    public class UserInterfaceContext : ApplicationContext, IDisposable
    {
        static TimeSpan MIN_EXPLORER_RUNTIME = TimeSpan.FromSeconds(10);

        ILogger<UserInterfaceContext> Logger { get; set; }

        private SynchronizationContext _syncContext;

        private Control _control;

        public UserInterfaceContext(ILogger<UserInterfaceContext> logger)
        {
            Logger = logger;

            StartUIThread();
        }

        private void StartUIThread()
        {
            using (ManualResetEvent wait = new ManualResetEvent(false))
            {
                void FinishStartup(object sender, EventArgs e)
                {
                    Application.Idle -= FinishStartup;

                    _syncContext = SynchronizationContext.Current;

                    _control = new Button();

                    Logger.LogDebug($"started; DPI={DPI[0]}/{DPI[1]}; scaling={Scaling}");

                    wait.Set();
                }

                Thread thread = new Thread(() =>
                {
                    SetProcessDPIAware();

                    //SetPreferredAppMode(PreferredAppMode.AllowDark);

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Idle += FinishStartup;
                    Application.Run(this);
                });

                thread.Name = GetType().Name;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                wait.WaitOne();
            }
        }

        private Process Explorer => Process.GetProcessesByName("explorer").Where(p => p.SessionId == Process.GetCurrentProcess().SessionId).FirstOrDefault();

        private async Task WaitIfNecessary()
        {
            if (_syncContext == null)
                throw new InvalidOperationException("No SynchronizationContext");

            while (Explorer == null)
            {
                await Task.Delay(1000);
            }

            if (Explorer.Runtime() < MIN_EXPLORER_RUNTIME)
            {
                await Task.Delay(MIN_EXPLORER_RUNTIME - Explorer.Runtime());
            }
        }

        public float[] DPI
        {
            get
            {
                using (Graphics g = _control.CreateGraphics())
                {
                    return new float[] { g.DpiX, g.DpiY };
                }
            }
        }

        public float Scaling => DPI.Average() / 96.0f;

        #region Synchronization with UI-Thread
        public void SendAction(Action action)
        {
            WaitIfNecessary().Wait();

            _syncContext.Send(delegate { action(); }, null);
        }
        public async Task SendActionAsync(Action action)
        {
            await SendFunctionAsync(() => { action(); return true; });
        }
        public async Task<T> SendFunctionAsync<T>(Func<T> function)
        {
            await WaitIfNecessary();

            var taskSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            _syncContext.Post(delegate
            {
                try
                {
                    var result = function();

                    taskSource.SetResult(result);
                }
                catch (Exception e)
                {
                    taskSource.SetException(e);
                }
            }, null);

            return await taskSource.Task;
        }
        public void PostAction(Action action)
        {
            WaitIfNecessary().Wait();

            _syncContext.Post(delegate { action(); }, null);
        }
        #endregion

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            Logger.LogDebug($"stopped");
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(PreferredAppMode appMode);
        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        public static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);
        [DllImport("uxtheme.dll", EntryPoint = "#138", SetLastError = true)]
        private static extern bool ShouldAppsUseDarkMode();

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        // Structs for immersive dark title bar (Windows 11+)
        private enum WindowCompositionAttribute
        {
            WCA_USEDARKMODECOLORS = 26
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }


        public enum PreferredAppMode
        {
            Default,
            AllowDark,
            ForceDark,
            ForceLight,
            Max
        }

        public static void EnableDarkMode(Form form)
        {
            // Set app-wide preferred mode
            SetPreferredAppMode(PreferredAppMode.AllowDark);

            // Enable dark mode for this window
            AllowDarkModeForWindow(form.Handle, true);

            // Dark title bar support (Windows 11+)
            UseImmersiveDarkMode(form.Handle, true);
        }

        private static void UseImmersiveDarkMode(IntPtr handle, bool enabled)
        {
            if (Environment.OSVersion.Version.Build >= 18362) // Windows 10 1903+
            {
                int useDark = enabled ? 1 : 0;
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_USEDARKMODECOLORS,
                    SizeOfData = Marshal.SizeOf(useDark),
                    Data = Marshal.AllocHGlobal(sizeof(int))
                };
                Marshal.WriteInt32(data.Data, useDark);
                SetWindowCompositionAttribute(handle, ref data);
                Marshal.FreeHGlobal(data.Data);
            }
        }

    }

    static class ProcessExt
    {
        public static TimeSpan Runtime(this Process process)
        {
            return DateTime.Now - process.StartTime;
        }
    }
}