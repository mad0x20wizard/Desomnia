using BrightIdeasSoftware;
using DesomniaSessionMinion.Tools;
using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Messages;
using ObjectListViewDemo;
using ObjectListViewDemo.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace MadWizard.Desomnia.Minion
{
    public partial class SleeplessConfigurationWindow : Form
    {
        public SleeplessConfigurationWindow()
        {
            InitializeComponent();

            treeListViewTokens.ContextMenu = CreateContextMenu();

            //SetWindowTheme(treeViewTokens.Handle, "Explorer", null);

            SetupColumns();
            SetupTree();
        }

        public DateTime? LastInspection { get; set; }
        public DateTime? NextInspection { get; set; }

        private ContextMenu CreateContextMenu()
        {
            var context = new ContextMenu();

            context.Popup += (sender, args) =>
            {
                context.MenuItems.Clear();

                var itemRefresh = context.MenuItems.Add("Aktualisieren");

                itemRefresh.Click += (s, a) =>
                {
                    InspectionRequested?.Invoke(this, EventArgs.Empty);
                };
            };

            return context.WithMenuItemImages();
        }


        private void SetupTree()
        {
            // TreeListView require two delegates:
            // 1. CanExpandGetter - Can a particular model be expanded?
            // 2. ChildrenGetter - Once the CanExpandGetter returns true, ChildrenGetter should return the list of children

            // CanExpandGetter is called very often! It must be very fast.

            this.treeListViewTokens.CanExpandGetter = delegate (object x)
            {
                return ((UsageTokenInfo)x).Tokens.Count > 0;
            };

            // We just want to get the children of the given directory.
            // This becomes a little complicated when we can't (for whatever reason). We need to report the error 
            // to the user, but we can't just call MessageBox.Show() directly, since that would stall the UI thread
            // leaving the tree in a potentially undefined state (not good). We also don't want to keep trying to
            // get the contents of the given directory if the tree is refreshed. To get around the first problem,
            // we immediately return an empty list of children and use BeginInvoke to show the MessageBox at the 
            // earliest opportunity. We get around the second problem by collapsing the branch again, so it's children
            // will not be fetched when the tree is refreshed. The user could still explicitly unroll it again --
            // that's their problem :)
            this.treeListViewTokens.ChildrenGetter = delegate (object x) {
                    return ((UsageTokenInfo)x).Tokens;
            };

            // Once those two delegates are in place, the TreeListView starts working
            // after setting the Roots property.

            // List all drives as the roots of the tree
            //ArrayList roots = new ArrayList();
            //foreach (DriveInfo di in DriveInfo.GetDrives())
            //{
            //    if (di.IsReady)
            //        roots.Add(new MyFileSystemInfo(new DirectoryInfo(di.Name)));
            //}


            //this.treeListViewTokens.Roots = roots;

        }

        private void SetupColumns()
        {
            // The column setup here is identical to the File Explorer example tab --
            // nothing specific to the TreeListView. 

            // The only difference is that we don't setup anything to do with grouping,
            // since TreeListViews can't show groups.

            //SysImageListHelper helper = new SysImageListHelper(this.treeListViewTokens);
            //this.olvColumnName.ImageGetter = delegate (object x) {
            //    return helper.GetImageIndex(((MyFileSystemInfo)x).FullName);
            //};

            // Show the system description for this object
            this.olvColumnName.AspectGetter = delegate (object x) {
                return ((UsageTokenInfo)x).DisplayName;
            };

            // Show the system description for this object
            this.olvColumnType.AspectGetter = delegate (object x) {
                return ((UsageTokenInfo)x).TypeName;
            };

        }

        public event EventHandler SleeplessChanged;
        public event EventHandler InspectionRequested;

        public DateTime? ShouldBeSleeplessUntil
        {
            get
            {
                if (checkBoxPermanent.Checked)
                    return DateTime.MaxValue;
                else if (checkBoxTime.Checked)
                    return dateTimePicker.Value;
                else
                    return null;
            }

            set
            {
                dateTimePicker.MinDate = DateTime.Now;
                dateTimePicker.Value = DateTime.Now;

                if (value != null && value < DateTime.Now)
                    value = null;

                if (value == null)
                {
                    checkBoxPermanent.Checked = false;
                    checkBoxTime.Checked = false;
                }
                else if (value == DateTime.MaxValue)
                {
                    checkBoxPermanent.Checked = true;
                    checkBoxTime.Checked = false;
                }
                else
                {
                    checkBoxPermanent.Checked = false;
                    checkBoxTime.Checked = true;

                    dateTimePicker.Value = value.Value;
                }
            }
        }

        public bool? ShouldBeSleeplessIfUsage
        {
            get
            {
                if (checkBoxUsage.Enabled)
                    return checkBoxUsage.Checked;
                else
                    return null;
            }

            set
            {
                if (checkBoxUsage.Enabled = value != null)
                {
                    checkBoxUsage.Checked = value.Value;
                }
                else
                {
                    checkBoxUsage.Checked = false;
                }
            }
        }

        public IList<UsageTokenInfo> Tokens
        {
            get { return new List<UsageTokenInfo>(); }

            set
            {
                treeListViewTokens.Roots = value;
            }
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            SleeplessChanged?.Invoke(this, EventArgs.Empty);

            Close();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void checkBoxTime_CheckedChanged(object sender, EventArgs e)
        {
            groupBox1.Enabled = checkBoxTime.Checked;

            if (checkBoxTime.Checked)
                checkBoxPermanent.Checked = false;

        }

        private void checkBoxPermanent_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxPermanent.Checked)
                checkBoxTime.Checked = false;
        }

        private void checkBoxUsage_CheckedChanged(object sender, EventArgs e)
        {
            //groupBoxUsage.Enabled = checkBoxUsage.Checked;
        }

        private void progressBar1_Click(object sender, EventArgs e)
        {

        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr w, IntPtr l);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private extern static int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private void timer_Tick(object sender, EventArgs e)
        {
            int min = 0, max, value;

            if (LastInspection != null && NextInspection != null)
            {
                max = (int)(NextInspection - LastInspection).Value.TotalMilliseconds;
                value = (int)(DateTime.Now - LastInspection).Value.TotalMilliseconds;

                if (value > max)
                {
                    progressBarInspection.SetColor(ProgressBarColor.Yellow);
                    progressBarInspection.Cursor = Cursors.WaitCursor;


                    value = max;
                }
                else
                {
                    progressBarInspection.SetColor(ProgressBarColor.Green);
                    progressBarInspection.Cursor = Cursors.AppStarting;
                }
            }
            else
            {
                progressBarInspection.SetColor(ProgressBarColor.Red);
                progressBarInspection.Cursor = Cursors.No;

                max = value = 100;
            }

            progressBarInspection.Minimum = min;
            progressBarInspection.Maximum = max;
            progressBarInspection.Value = value;

        }
    }

    public static class ControlExtensions
    {
        public static void SetParentWithSameScreenCoordinates(this Control source, Control parent)
        {
            source.Location = parent.PointToClient(source.PointToScreen(Point.Empty));
            source.Parent = parent;
        }
    }

}
