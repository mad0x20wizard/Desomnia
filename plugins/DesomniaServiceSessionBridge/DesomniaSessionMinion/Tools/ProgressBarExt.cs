using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesomniaSessionMinion.Tools
{
    static class ProgressBarExt
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr w, IntPtr l);

        public static void SetColor(this ProgressBar pBar, ProgressBarColor color)
        {
            SendMessage(pBar.Handle, 1040, (IntPtr)color, IntPtr.Zero);
        }
    }

    internal enum ProgressBarColor
    {
        Green = 1,
        Red = 2,
        Yellow = 3,
    }
}
