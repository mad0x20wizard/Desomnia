using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    /// <summary>
    /// Minimal IOKit bindings. io_object_t and friends are mach port names (32-bit).
    /// IOKit needs no WindowServer connection, which is why both the display and the power
    /// manager are built on it — validated from a root SSH shell by the probes/ projects.
    /// </summary>
    public static partial class IOKit
    {
        /// <summary>The IOKit framework path — also the home of the IOPMLib sub-API (see <see cref="IOPM"/>).</summary>
        public const string Framework = "/System/Library/Frameworks/IOKit.framework/IOKit";

        public const uint kIOMainPortDefault = 0;

        public const string kIOFirstMatchNotification = "IOServiceFirstMatch";
        public const string kIOTerminatedNotification = "IOServiceTerminate";
        public const string kIOGeneralInterest = "IOGeneralInterest";

        #region P/Invoke
        [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint IOServiceMatching(string className); // returns CFMutableDictionaryRef

        [LibraryImport(Framework)]
        public static partial int IOServiceGetMatchingServices(uint mainPort, nint matching /*consumed*/, out uint iterator);

        [LibraryImport(Framework)]
        public static partial uint IOIteratorNext(uint iterator);

        [LibraryImport(Framework)]
        public static partial int IOObjectRelease(uint obj);

        [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int IORegistryEntryGetChildIterator(uint entry, string plane, out uint iterator);

        [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
        public static partial uint IOObjectConformsTo(uint obj, string className);

        [LibraryImport(Framework)]
        public static partial int IORegistryEntryGetRegistryEntryID(uint entry, out ulong id);

        [LibraryImport(Framework)]
        public static partial int IOServiceClose(uint connect);

        [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
        private static partial int IORegistryEntryGetPath(uint entry, string plane, [Out] byte[] path /*512*/);

        [LibraryImport(Framework)]
        private static partial nint IORegistryEntryCreateCFProperty(uint entry, nint key, nint allocator, uint options);

        [LibraryImport(Framework)]
        public static partial nint IONotificationPortCreate(uint mainPort);

        [LibraryImport(Framework)]
        public static partial void IONotificationPortDestroy(nint notifyPort);

        [LibraryImport(Framework)]
        public static partial nint IONotificationPortGetRunLoopSource(nint notifyPort);

        // callbacks are UnmanagedCallersOnly function pointers (AOT-safe, no delegate marshalling):
        //   matching: void (*)(void* refCon, io_iterator_t iterator)
        //   interest: void (*)(void* refCon, io_service_t service, uint32_t messageType, void* messageArgument)
        [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int IOServiceAddMatchingNotification(nint notifyPort, string notificationType, nint matching /*consumed*/, nint callback, nint refCon, out uint iterator);

        [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int IOServiceAddInterestNotification(nint notifyPort, uint service, string interestType, nint callback, nint refCon, out uint notification);
        #endregion

        #region helpers
        public static string GetPath(uint entry, string plane = "IOService")
        {
            var buffer = new byte[512];

            if (IORegistryEntryGetPath(entry, plane, buffer) != 0)
                return string.Empty;

            return CF.DecodeUtf8(buffer);
        }

        /// <summary>Fetches a registry property; the returned CFTypeRef is owned by the caller (release it).</summary>
        public static nint GetProperty(uint entry, string key)
        {
            nint cfKey = CF.CreateString(key);

            try
            {
                return IORegistryEntryCreateCFProperty(entry, cfKey, 0, 0);
            }
            finally
            {
                CF.CFRelease(cfKey);
            }
        }

        public static string? GetStringProperty(uint entry, string key)
        {
            nint value = GetProperty(entry, key);

            if (value == 0)
                return null;

            try
            {
                return CF.ToManagedString(value);
            }
            finally
            {
                CF.CFRelease(value);
            }
        }

        public static bool? GetBooleanProperty(uint entry, string key)
        {
            nint value = GetProperty(entry, key);

            if (value == 0)
                return null;

            try
            {
                return CF.CFGetTypeID(value) == CF.CFBooleanGetTypeID() ? CF.CFBooleanGetValue(value) : null;
            }
            finally
            {
                CF.CFRelease(value);
            }
        }

        /// <summary>Finds the first service matching the given IOKit class; caller must IOObjectRelease.</summary>
        public static uint FindService(string className)
        {
            if (IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching(className), out uint iterator) != 0)
                return 0;

            try
            {
                return IOIteratorNext(iterator);
            }
            finally
            {
                IOObjectRelease(iterator);
            }
        }

        /// <summary>Returns every currently matchable service of a class; the caller owns each entry.</summary>
        public static IEnumerable<uint> FindServices(string className)
        {
            nint matching = IOServiceMatching(className);

            if (matching == 0 || IOServiceGetMatchingServices(kIOMainPortDefault, matching, out uint iterator) != 0)
                yield break;

            try
            {
                while (IOIteratorNext(iterator) is uint service && service != 0)
                    yield return service;
            }
            finally
            {
                IOObjectRelease(iterator);
            }
        }
        #endregion
    }
}
