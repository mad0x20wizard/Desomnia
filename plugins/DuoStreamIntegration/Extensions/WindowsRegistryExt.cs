namespace Microsoft.Win32
{
    internal static class WindowsRegistryExt
    {
        extension (RegistryKey key)
        {
            public object? this[string name]
            {
                get => key.GetValue(name);

                set
                {
                    if (value != null)
                    {
                        key.SetValue(name, value);
                    }
                    else if (key.GetValue(name) != null)
                    {
                        try
                        {
                            key.DeleteValue(name);
                        }
                        catch (ArgumentException)
                        {
                            /* No value exists with that name. */
                        }
                    }
                }
            }
        }
    }
}
