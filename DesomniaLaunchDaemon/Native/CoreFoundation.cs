using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    /// <summary>
    /// Minimal CoreFoundation bindings with typed accessors for IORegistry property dictionaries.
    /// Public so macOS-native plugins referencing the daemon can reuse the bindings.
    /// </summary>
    public static partial class CF
    {
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        public const uint kCFStringEncodingUTF8 = 0x08000100;
        public const long kCFNumberSInt64Type = 4;

        #region P/Invoke
        [LibraryImport(CoreFoundation)]
        public static partial void CFRelease(nint cf);

        [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint CFStringCreateWithCString(nint allocator, string str, uint encoding);

        [LibraryImport(CoreFoundation)]
        private static partial nint CFStringGetLength(nint str);

        [LibraryImport(CoreFoundation)]
        private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

        [LibraryImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool CFStringGetCString(nint str, [Out] byte[] buffer, nint bufferSize, uint encoding);

        [LibraryImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static partial bool CFBooleanGetValue(nint boolean);

        [LibraryImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool CFNumberGetValue(nint number, long type, out long value);

        [LibraryImport(CoreFoundation)]
        public static partial nint CFUUIDCreateFromString(nint allocator, nint uuidString /*CFString*/); // returns CFUUIDRef (+1)

        [LibraryImport(CoreFoundation)]
        public static partial nint CFUUIDCreateString(nint allocator, nint uuid /*CFUUIDRef*/); // returns CFString (+1)

        [LibraryImport(CoreFoundation)]
        private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

        [LibraryImport(CoreFoundation)]
        private static partial nint CFDictionaryGetCount(nint dictionary);

        [LibraryImport(CoreFoundation)]
        private static partial void CFDictionaryGetKeysAndValues(nint dictionary, [Out] nint[] keys, [Out] nint[] values);

        [LibraryImport(CoreFoundation)]
        public static partial nint CFArrayGetCount(nint array);

        [LibraryImport(CoreFoundation)]
        public static partial nint CFArrayGetValueAtIndex(nint array, nint index);

        [LibraryImport(CoreFoundation)]
        private static partial nuint CFArrayGetTypeID();

        [LibraryImport(CoreFoundation)]
        public static partial nuint CFGetTypeID(nint cf);

        [LibraryImport(CoreFoundation)]
        public static partial nuint CFStringGetTypeID();

        [LibraryImport(CoreFoundation)]
        public static partial nuint CFBooleanGetTypeID();

        [LibraryImport(CoreFoundation)]
        private static partial nuint CFNumberGetTypeID();

        [LibraryImport(CoreFoundation)]
        private static partial nuint CFDictionaryGetTypeID();

        [LibraryImport(CoreFoundation)]
        public static partial nint CFRunLoopGetCurrent();

        [LibraryImport(CoreFoundation)]
        public static partial void CFRunLoopRun();

        [LibraryImport(CoreFoundation)]
        public static partial void CFRunLoopStop(nint runLoop);

        [LibraryImport(CoreFoundation)]
        public static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);
        #endregion

        /// <summary>The run loop mode CFString; created once, never released.</summary>
        public static nint RunLoopDefaultMode => field != 0 ? field : field = CreateString("kCFRunLoopDefaultMode");

        public static nint CreateString(string value)
        {
            return CFStringCreateWithCString(0, value, kCFStringEncodingUTF8);
        }

        /// <summary>Decodes a NUL-terminated UTF-8 buffer (the shape all C-string out-buffers share).</summary>
        public static string DecodeUtf8(byte[] buffer)
        {
            int end = Array.IndexOf(buffer, (byte)0);

            return System.Text.Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
        }

        public static string? ToManagedString(nint cfString)
        {
            if (cfString == 0 || CFGetTypeID(cfString) != CFStringGetTypeID())
                return null;

            nint capacity = CFStringGetMaximumSizeForEncoding(CFStringGetLength(cfString), kCFStringEncodingUTF8) + 1;

            var buffer = new byte[capacity];

            if (!CFStringGetCString(cfString, buffer, capacity, kCFStringEncodingUTF8))
                return null;

            return DecodeUtf8(buffer);
        }

        public static long? ToNumber(nint cf)
        {
            if (cf == 0 || CFGetTypeID(cf) != CFNumberGetTypeID())
                return null;

            return CFNumberGetValue(cf, kCFNumberSInt64Type, out long value) ? value : null;
        }

        public static bool IsArray(nint cf)
        {
            return cf != 0 && CFGetTypeID(cf) == CFArrayGetTypeID();
        }

        public static bool IsDictionary(nint cf)
        {
            return cf != 0 && CFGetTypeID(cf) == CFDictionaryGetTypeID();
        }

        #region dictionary accessors (CF "get rule": returned values are borrowed, not owned)
        /// <summary>Snapshots the entries of a CFDictionary (borrowed references, valid while the dictionary lives).</summary>
        public static (nint[] Keys, nint[] Values) GetKeysAndValues(nint dictionary)
        {
            nint count = CFDictionaryGetCount(dictionary);

            if (count <= 0)
                return ([], []);

            var keys = new nint[count];
            var values = new nint[count];

            CFDictionaryGetKeysAndValues(dictionary, keys, values);

            return (keys, values);
        }

        private static nint GetValue(nint dictionary, string key)
        {
            nint cfKey = CreateString(key);

            try
            {
                return CFDictionaryGetValue(dictionary, cfKey);
            }
            finally
            {
                CFRelease(cfKey);
            }
        }

        public static string? GetString(nint dictionary, string key)
        {
            return ToManagedString(GetValue(dictionary, key));
        }

        public static long? GetNumber(nint dictionary, string key)
        {
            return ToNumber(GetValue(dictionary, key));
        }

        public static nint GetDictionary(nint dictionary, string key)
        {
            nint value = GetValue(dictionary, key);

            return value != 0 && CFGetTypeID(value) == CFDictionaryGetTypeID() ? value : 0;
        }
        #endregion
    }
}
