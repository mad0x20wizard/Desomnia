using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes.Manager
{
    public partial class Win32ProcessManager : ListenerAwareProcessManager
    {
        /**
         * The processor time this process has used, or null once it can no longer be sampled.
         *
         * Both halves come back as durations in the same 100-nanosecond units a TimeSpan counts in,
         * so they need no conversion – only adding up, the way the BCL's TotalProcessorTime is the
         * sum of its two. Verified equal to it, tick for tick, on a live process.
         */
        internal static TimeSpan? QueryProcessorTime(int pid)
        {
            using var process = OpenHandle(pid);

            if (process.IsInvalid || !GetProcessTimes(process, out _, out _, out long kernel, out long user))
                return null;

            return TimeSpan.FromTicks(kernel + user);
        }

        /**
         * The IO the process has performed since it started, or null once it can no longer be asked.
         *
         * The kernel keeps six counters; this takes the read/write pair and leaves the third bucket
         * deliberately: Other is where every Winsock transfer lands (sockets are driven through
         * NtDeviceIoControlFile), mixed indistinguishably with plain IOCTL chatter – counting it
         * would make minIO trip on a download here and nowhere else. Network demand has its own
         * attribute, measured by something that can actually see the network.
         *
         * What remains is logical file and device IO as the process performed it: reads served from
         * the cache count in full, memory-mapped IO not at all – the counters are accounted at the
         * syscall, and mapped pages never make one.
         */
        internal static ProcessInputOutput? QueryIO(int pid)
        {
            using var process = OpenHandle(pid);

            if (process.IsInvalid || !GetProcessIoCounters(process, out IO_COUNTERS counters))
                return null;

            return new ProcessInputOutput((long)counters.ReadTransferCount, (long)counters.WriteTransferCount);
        }

        /**
         * The passive traffic approximation: the third pair of the same six counters QueryIO
         * reads. All Winsock transfers land in the Other bucket (sockets are driven through
         * NtDeviceIoControlFile), so its growth is a cheap "this process is talking to the
         * network" signal – one syscall, no standing cost, and honest about being approximate:
         * fast-path receives escape the accounting, every non-socket IOCTL lands in the same
         * numbers, and the bucket knows no direction, so the whole count rides the received
         * side. The precise meter (<?global ProcessManager:watchTraffic="active"?>) books its
         * payload bytes straight into the process, which answers with those instead – this is
         * the fallback for every process nothing books into.
         */
        internal static ProcessInputOutput? QueryTraffic(int pid)
        {
            using var process = OpenHandle(pid);

            if (process.IsInvalid || !GetProcessIoCounters(process, out IO_COUNTERS counters))
                return null;

            return new ProcessInputOutput((long)counters.OtherTransferCount, 0);
        }

        /**
         * Whether the process has ended – or null when that cannot be told from here.
         *
         * A process handle is signalled once the process it names has terminated, which is the whole
         * question, so a wait of zero answers it outright. The exit code would answer it too, but only
         * almost: STILL_ACTIVE is 259 and 259 is also a perfectly legal thing to exit with, so a
         * process that chose it would read as running for ever. Nothing to weigh up – this is one
         * syscall either way.
         */
        internal static bool? QueryHasStopped(int pid)
        {
            // SYNCHRONIZE alone, which is all a wait needs and is granted for more processes than more
            using var process = OpenHandle(pid, SYNCHRONIZE);

            if (process.IsInvalid)
            {
                // a pid the kernel does not know has certainly ended; one we may not open, we cannot say
                return Marshal.GetLastWin32Error() == ERROR_INVALID_PARAMETER ? true : null;
            }

            switch (WaitForSingleObject(process, 0))
            {
                case WAIT_OBJECT_0:
                    return true;
                case WAIT_TIMEOUT:
                    return false;

                default:
                    return null; // the wait itself failed, so this is no answer about the process
            }
        }

        /**
         * The image file name as System.Diagnostics.Process spells it: with a trailing ".exe" taken
         * off, and only ".exe".
         *
         * Approximating this with GetFileNameWithoutExtension would be wrong in a way nobody would
         * notice for months: this name is what the configured patterns match against, and something
         * called "foo.bar" must not quietly become "foo".
         */
        internal static string ProcessNameOf(ReadOnlySpan<char> fileName)
        {
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return fileName[..^4].ToString();
            else
                return fileName.ToString();
        }

        /**
         * Describes a process from its pid: a name and a session id, two questions on one handle.
         *
         * The session id is what this is for. System.Diagnostics.Process answers ProcessName from a
         * handle much like the below and is cheap at it, but it has no such shortcut for SessionId:
         * that one goes through the kernel's whole process list, which .NET then turns into an
         * object per process before reading the single number it was asked for. Measured on a
         * machine with 478 processes, describing one pid: 15 us here against 6.2 ms through the BCL.
         *
         * Deliberately not the parent as well, cheap though it would be from the same handle: it is
         * QueryParentProcess that verifies the parent against pid reuse, and TriggerStart calls that
         * only when ParentId is still unknown. Filling it in here would skip the check.
         */
        protected override ProcessInformation? QueryProcess(int pid)
        {
            using var process = OpenHandle(pid);

            if (!process.IsInvalid)
            {
                // Both, or neither: ProcessHandle reads a missing session id off the BCL object, which
                // it materializes to do – so half a description here would quietly cost more than no
                // description at all, and cost it inside a constructor where nothing expects a syscall.
                if (QueryImagePath(process) is string imagePath && QuerySessionId(process) is int sessionId)
                {
                    return new(pid)
                    {
                        Name = ProcessNameOf(Path.GetFileName(imagePath.AsSpan())),
                        SessionId = sessionId,
                        ImagePath = imagePath
                    };
                }
            }

            return base.QueryProcess(pid);
        }

        /// <summary>The full image path, or null for a process that has none (or has since gone).</summary>
        private static string? QueryImagePath(SafeProcessHandle process)
        {
            static string? Read(SafeProcessHandle process, uint capacity)
            {
                var buffer = new char[capacity];
                uint size = capacity;

                return QueryFullProcessImageName(process, 0, buffer, ref size) ? new(buffer, 0, (int)size) : null;
            }

            // The first size covers every image path a machine actually runs; the retry is only so
            // that "every" needs no defending – a longer one costs a second call, not the process.
            return Read(process, 512) ?? (Marshal.GetLastWin32Error() == ERROR_INSUFFICIENT_BUFFER ? Read(process, LONG_PATH) : null);
        }

        /// <summary>The Terminal Services session the process belongs to.</summary>
        private static int? QuerySessionId(SafeProcessHandle process)
        {
            if (NtQueryInformationProcess(process, ProcessSessionInformation, out uint session, sizeof(uint), out _) != STATUS_SUCCESS)
                return null;

            return (int)session;
        }

        /**
         * The ancestry, answered from a pid alone.
         *
         * Everything this needs comes off a short-lived handle opened for two questions, so an ETW
         * start – which reports nothing but ids – is as good a starting point as a materialized
         * process. It is also the cheaper way round: the ProcessInfo a System.Diagnostics.Process
         * builds to answer either question walks every thread of the process, and it opens for full
         * access to do it, which the protected processes deny outright.
         */
        protected override ProcessInformation? QueryParentProcess(ProcessInformation info)
        {
            using var process = OpenHandle(info.Id);

            // the type is spelled out because the info class alone does not tell the overloads apart
            if (!process.IsInvalid && NtQueryInformationProcess(process, ProcessBasicInformation, out PROCESS_BASIC_INFORMATION basic, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) == STATUS_SUCCESS)
            {
                var parentId = basic.InheritedFromUniqueProcessId.ToInt32();

                using var parent = OpenHandle(parentId);

                /**
                 * Windows hands its pids out again, so the id a process recorded as its parent may since
                 * have been taken over by an unrelated one – which can leave two processes claiming to
                 * be each other's ancestor. A parent that started after its child is one of those, and
                 * no parent at all.
                 */
                if (parent.IsInvalid
                    || !GetProcessTimes(parent, out long createdParent, out _, out _, out _)
                    || !GetProcessTimes(process, out long createdChild, out _, out _, out _)
                    || createdParent > createdChild)
                    return null;

                return parentId;
            }

            return null;
        }

        #region Windows-API
        const int STATUS_SUCCESS = 0;

        const int ProcessBasicInformation = 0;
        const int ProcessSessionInformation = 24;

        const int ERROR_INVALID_PARAMETER = 87;
        const int ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>The longest path Windows will hold at all, once the MAX_PATH limit is lifted.</summary>
        const uint LONG_PATH = 32767;

        /// <summary>Enough for every question asked here, and granted for far more processes than full access is.</summary>
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>Enough to wait on a process, and nothing else.</summary>
        internal const uint SYNCHRONIZE = 0x00100000;

        const uint WAIT_OBJECT_0 = 0;
        const uint WAIT_TIMEOUT = 0x102;

        /// <summary>Opens a process for the queries above – invalid if it is gone, or protected from us.</summary>
        private static SafeProcessHandle OpenHandle(int pid, uint access = PROCESS_QUERY_LIMITED_INFORMATION) => new(OpenProcess(access, false, pid), ownsHandle: true);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetProcessTimes(SafeProcessHandle processHandle, out long creation, out long exit, out long kernel, out long user);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetProcessIoCounters(SafeProcessHandle processHandle, out IO_COUNTERS counters);

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            internal ulong ReadOperationCount;
            internal ulong WriteOperationCount;
            internal ulong OtherOperationCount;
            internal ulong ReadTransferCount;
            internal ulong WriteTransferCount;
            internal ulong OtherTransferCount;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool QueryFullProcessImageName(SafeProcessHandle processHandle, uint flags, [Out] char[] imageName, ref uint size);

        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(SafeProcessHandle processHandle, int processInformationClass, out PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(SafeProcessHandle processHandle, int processInformationClass, out uint processInformation, int processInformationLength, out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            internal nint Reserved1;
            internal nint PebBaseAddress;
            internal nint Reserved2_0;
            internal nint Reserved2_1;
            internal nint UniqueProcessId;
            internal nint InheritedFromUniqueProcessId;
        }
        #endregion
    }
}
