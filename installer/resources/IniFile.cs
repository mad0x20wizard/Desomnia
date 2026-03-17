using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public class IniFile
{
    private readonly Dictionary<string, Section> sections = new Dictionary<string, Section>();

    public IniFile(string path)
    {
        var buffer = new char[32768];

        var count = GetPrivateProfileSectionNames(buffer, buffer.Length, path);

        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                int start = i;
                while (i < count && buffer[i] != '\0')
                    i++;

                if (i > start)
                {
                    var name = new string(buffer, start, i - start);

                    sections[name] = new Section(path, name);
                }
            }
        }
    }

    public Section this[string name]
    {
        get
        {
            if (sections.ContainsKey(name))
                return sections[name];
            else
                return null;
        }
    }

    public class Section : IEnumerable<string>
    {
        private string path;
        private string name;

        public Section(string path, string name)
        {
            this.path = path;
            this.name = name;
        }

        public string this[string key]
        {
            get
            {
                var buffer = new char[32768];

                var charCount = GetPrivateProfileString(name, key, "", buffer, buffer.Length, path);

                return charCount > 0 ? new string(buffer, 0, charCount) : string.Empty;
            }

            set
            {
                if (value == null)
                    return;

                if (WritePrivateProfileString(name, key, value, path) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            var buffer = new char[32768];
            var count = GetPrivateProfileString(name, null, "", buffer, buffer.Length, path);

            var keys = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int start = i;
                while (i < count && buffer[i] != '\0')
                    i++;

                if (i > start)
                    keys.Add(new string(buffer, start, i - start));
            }

            return keys.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern int GetPrivateProfileSectionNames(char[] value, int maxSize, string filePath);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPrivateProfileString(string section, string key, string defaultValue, char[] value, int maxSize, string filePath);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WritePrivateProfileString(string section, string key, string value, string filePath);
}
