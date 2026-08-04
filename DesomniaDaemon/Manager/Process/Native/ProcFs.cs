namespace MadWizard.Desomnia.Processes.Manager.Native
{
    /// <summary>
    /// The parts of procfs the process monitor needs. There is no P/Invoke here and none is wanted:
    /// on Linux the process table is a directory and everything worth knowing about a process is a
    /// single line of text, which is exactly the cheapness the BCL enumeration throws away.
    ///
    /// Public so Linux-native plugins referencing the daemon can reuse it.
    /// </summary>
    public static class ProcFs
    {
        public const string Root = "/proc";

        /// <summary>The process state procfs reports for a process that has exited but not been reaped.</summary>
        public const char Zombie = 'Z';

        /// <summary>The fields of /proc/[pid]/stat this monitor reads. See proc(5).</summary>
        public readonly record struct Stat(string Command, char State, int ParentId, int SessionId);

        /// <summary>The storage-layer fields of /proc/[pid]/io. See proc_pid_io(5).</summary>
        public readonly record struct IO(long ReadBytes, long WriteBytes);

        /// <summary>The ids of every process on the machine — one directory read, nothing per process.</summary>
        public static IEnumerable<int> EnumeratePIDs()
        {
            foreach (var directory in Directory.EnumerateDirectories(Root))
            {
                // /proc holds plenty that is not a process; a numeric name is what makes one
                if (int.TryParse(Path.GetFileName(directory), out int pid))
                    yield return pid;
            }
        }

        public static Stat? ReadStat(int pid)
        {
            try
            {
                return ParseStat(File.ReadAllText($"{Root}/{pid}/stat"));
            }
            catch (Exception)
            {
                return null; // gone between the listing and the read, or not ours to read
            }
        }

        /// <summary>
        /// Parses a /proc/[pid]/stat line: "pid (comm) state ppid pgrp session …".
        /// </summary>
        /// <remarks>
        /// The command is arbitrary bytes chosen by the process itself and may contain spaces and
        /// parentheses of its own, so it is delimited by the LAST ')' on the line rather than split
        /// on whitespace — which is what makes a naïve field split wrong for exactly the processes
        /// most likely to be watched.
        /// </remarks>
        public static Stat? ParseStat(string line)
        {
            int open = line.IndexOf('('), close = line.LastIndexOf(')');

            if (open < 0 || close < open)
                return null;

            var fields = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 4 || fields[0].Length == 0)
                return null;

            return new Stat
            (
                Command: line[(open + 1)..close],
                State: fields[0][0],
                ParentId: int.TryParse(fields[1], out int ppid) ? ppid : 0,
                SessionId: int.TryParse(fields[3], out int sid) ? sid : -1
            );
        }

        /**
         * The bytes a process has moved to and from the storage layer, or null where that cannot
         * be read — the process is gone again, or the kernel was built without
         * CONFIG_TASK_IO_ACCOUNTING and the file with it (every mainstream distro has it).
         *
         * read_bytes/write_bytes rather than rchar/wchar deliberately: the logical pair counts
         * every read() and write() on any descriptor — pipes, ttys and sockets included — and a
         * process busy on its sockets must not look disk-busy; separating those is the whole
         * point of having two attributes. The price is the cache: reads served from the page
         * cache never reach the block layer and are invisible here, as they are on macOS.
         * Buffered writes are credited to the writing process at dirty time, ahead of — and
         * regardless of — the flusher that eventually submits them.
         */
        public static IO? ReadIO(int pid)
        {
            try
            {
                return ParseIO(File.ReadAllText($"{Root}/{pid}/io"));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Parses /proc/[pid]/io — a handful of "key: value" lines.</summary>
        public static IO? ParseIO(string text)
        {
            long? read = null, write = null;

            foreach (var line in text.AsSpan().EnumerateLines())
            {
                if (line.StartsWith("read_bytes:"))
                    read = ParseValue(line);
                else if (line.StartsWith("write_bytes:"))
                    write = ParseValue(line);
            }

            return read is long r && write is long w ? new IO(r, w) : null;

            static long? ParseValue(ReadOnlySpan<char> line)
            {
                var value = line[(line.IndexOf(':') + 1)..].Trim();

                return long.TryParse(value, out long parsed) ? parsed : null;
            }
        }

        /**
         * The name a process answers to, given what procfs said and what its image is called.
         *
         * procfs keeps the command at 15 characters. The image is allowed to lengthen that name but
         * never to replace it: a process that renamed itself through prctl() means the name it
         * chose, so the image name is taken only where it confirms what procfs already said. That
         * is also what the BCL does with the command line, minus the truncation.
         */
        public static string ResolveName(string command, string? imagePath)
        {
            if (Path.GetFileName(imagePath) is not { Length: > 0 } image)
                return command;

            return image.Length > command.Length && image.StartsWith(command, StringComparison.Ordinal) ? image : command;
        }

        /// <summary>
        /// The path of a process' executable image, or null where there is none to read — kernel
        /// threads have no image, and an unprivileged reader may not follow the link at all.
        /// </summary>
        public static string? ReadExecutablePath(int pid)
        {
            try
            {
                if (new FileInfo($"{Root}/{pid}/exe").LinkTarget is not string target)
                    return null;

                // a binary replaced or removed while it is still running keeps its path, plus this
                const string Deleted = " (deleted)";

                return target.EndsWith(Deleted, StringComparison.Ordinal) ? target[..^Deleted.Length] : target;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
