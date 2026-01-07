using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Menu = System.Windows.Forms.Menu;

namespace MadWizard.Desomnia.Pipe
{
    public class ImageMenuItem : MenuItem
    {
        private Image _image;

        public ImageMenuItem() { }
        public ImageMenuItem(string text) : base(text) { }
        public ImageMenuItem(string text, Image image) : this(text)
        {
            Image = image;
        }

        public Image Image
        {
            get
            {
                return _image;
            }

            set
            {
                DeleteBitmap();

                if ((_image = value) != null)
                {
                    //convert to 32bppPArgb (the 'P' means The red, green, and blue components are premultiplied, according to the alpha component.)
                    using (Bitmap bitmap = new Bitmap(value.Width, value.Height, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap))
                            g.DrawImage(value, 0, 0, value.Width, value.Height);

                        GDIBitmap = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            DeleteBitmap();

            base.Dispose(disposing);
        }

        internal IntPtr GDIBitmap { get; private set; }

        private void DeleteBitmap()
        {
            if (GDIBitmap != IntPtr.Zero)
            {
                //Destroy old bitmap object
                DeleteObject(GDIBitmap);

                GDIBitmap = IntPtr.Zero;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }

    public static class ImageMenuExt
    {
        public static ContextMenu WithMenuItemImages(this ContextMenu menu) { menu.Popup += Menu_Popup; return menu; }
        public static MenuItem WithMenuItemImages(this MenuItem menu) { menu.Popup += Menu_Popup; return menu; }

        private static void Menu_Popup(object sender, EventArgs e)
        {
            UpdateMenuItemImages(sender as Menu);
        }

        private static void UpdateMenuItemImages(Menu menu)
        {
            /*
             * we have to track the menuPosition ourselves
             * because MenuItem.Index is only correct when
             * all the menu items are visible.
             */
            int visiblePos = 0;
            bool hasImageItems = false;
            foreach (MenuItem item in menu.MenuItems)
            {
                if (item.Visible)
                {
                    if (item is ImageMenuItem imageItem && imageItem.GDIBitmap != null)
                    {
                        MENUITEMINFO_T_RW menuItemInfo = new MENUITEMINFO_T_RW
                        {
                            hbmpItem = imageItem.GDIBitmap
                        };

                        SetMenuItemInfo(new HandleRef(null, menu.Handle), visiblePos, true, menuItemInfo);

                        hasImageItems = true;
                    }

                    UpdateMenuItemImages(item);

                    visiblePos++;
                }
            }

            if (hasImageItems)
            {
                MENUINFO mnuInfo = new MENUINFO();

                SetMenuInfo(new HandleRef(null, menu.Handle), mnuInfo);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SetMenuItemInfo(HandleRef hMenu, int uItem, bool fByPosition, MENUITEMINFO_T_RW lpmii);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SetMenuInfo(HandleRef hMenu, MENUINFO lpcmi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MENUITEMINFO_T_RW
        {
            public int cbSize = Marshal.SizeOf(typeof(MENUITEMINFO_T_RW));
            public int fMask = 0x00000080; //MIIM_BITMAP = 0x00000080
            public int fType;
            public int fState;
            public int wID;
            public IntPtr hSubMenu = IntPtr.Zero;
            public IntPtr hbmpChecked = IntPtr.Zero;
            public IntPtr hbmpUnchecked = IntPtr.Zero;
            public IntPtr dwItemData = IntPtr.Zero;
            public IntPtr dwTypeData = IntPtr.Zero;
            public int cch;
            public IntPtr hbmpItem = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MENUINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MENUINFO));
            public int fMask = 0x00000010; //MIM_STYLE;
            public int dwStyle = 0x04000000; //MNS_CHECKORBMP;
            public uint cyMax;
            public IntPtr hbrBack = IntPtr.Zero;
            public int dwContextHelpID;
            public IntPtr dwMenuData = IntPtr.Zero;
        }
    }

    public static class MenuItemEx
    {
        public static void UpdateShortcut(this MenuItem item, params Keys[] keys)
        {
            Shortcut shortcut = (Shortcut)keys.Aggregate(Keys.None, (s, k) => s |= k);

            var dataField = typeof(MenuItem).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
            var updateMenuItemMethod = typeof(MenuItem).GetMethod("UpdateMenuItem", BindingFlags.NonPublic | BindingFlags.Instance);
            var menuItemDataShortcutField = typeof(MenuItem).GetNestedType("MenuItemData", BindingFlags.NonPublic).GetField("shortcut", BindingFlags.NonPublic | BindingFlags.Instance);

            var zoomInData = dataField.GetValue(item);
            menuItemDataShortcutField.SetValue(zoomInData, shortcut);
            updateMenuItemMethod.Invoke(item, new object[] { true });
        }
    }
}
