using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// Writes a migrated document back to the configuration file (<c>autoMigrate="persistent"</c>):
    /// backup first, then the step is recorded in the run's migration protocol (the comment in
    /// front of the root, see <see cref="XMigrationProtocol"/>), then the document itself —
    /// rendered as faithfully as the XML writer allows (the document was parsed
    /// whitespace-preserving, so everything BETWEEN tags survives; whitespace inside start tags
    /// and quote styles are the writer's), in the file's own newline style, encoding and BOM
    /// state, replacing the file atomically (write next to it, then move over it) wherever
    /// the platform allows it — and rewriting it in place otherwise, notably on Windows while
    /// another reader still holds the file open, where <c>writeBackup</c> is the safety net.
    /// </summary>
    internal static class XMigrationFileWriter
    {
        const string TEMP_SUFFIX = ".tmp";

        static readonly byte[] UTF8_BOM = [0xEF, 0xBB, 0xBF];

        /// <summary>
        /// Writes the document to <paramref name="path"/> and returns the text that was written
        /// (as a reader of the file will see it, i.e. without a BOM).
        /// </summary>
        /// <param name="protocol">The run's migration protocol, which takes the step's record.</param>
        /// <param name="originalText">The raw text the document was parsed from — decides the newline style.</param>
        public static string Write(XDocument document, string path, string? backupPath, uint sourceVersion, uint targetVersion,
            IReadOnlyList<MigrationAnnotation> notes, XMigrationProtocol protocol, string originalText, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(protocol);

            // a symlinked configuration (a dotfiles setup, /etc pointing elsewhere) is updated
            // behind the link - moving a file over the link would replace the link itself
            if (new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true) is FileSystemInfo target)
                path = target.FullName;

            if (backupPath is not null)
            {
                // the bytes as they are on disk right now (for a later step: the previous step's output)
                if (string.Equals(Path.GetFullPath(backupPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"{MigrationSettings.WRITE_BACKUP_KEY} must not point at the configuration file itself ({path}).");

                File.Copy(path, backupPath, overwrite: true);
            }

            protocol.Record(document, sourceVersion, targetVersion, notes);

            var encoding = ResolveEncoding(document, hadBom: HasUtf8Bom(path));

            byte[] bytes = Render(document, encoding, newLine: ConfigurationText.DetectNewLine(originalText));

            Replace(path, bytes, logger);

            // decode the way File.ReadAllText will: BOM detected and dropped
            using var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true);

            return reader.ReadToEnd();
        }

        #region Rendering

        /// <summary>
        /// Renders the document as a reader of the file would see it — the declared encoding,
        /// the given BOM state and newline style. The serving path of a transient migration:
        /// what a persistent migration would write (minus the protocol comment) is what every
        /// consumer reads.
        /// </summary>
        internal static byte[] Render(XDocument document, bool hadBom, string newLine)
            => Render(document, ResolveEncoding(document, hadBom), newLine);

        /// <summary>Whether the content starts with a UTF-8 byte order mark.</summary>
        internal static bool HasUtf8Bom(ReadOnlySpan<byte> content)
            => content.Length >= UTF8_BOM.Length && content[..UTF8_BOM.Length].SequenceEqual(UTF8_BOM);

        private static Encoding ResolveEncoding(XDocument document, bool hadBom)
        {
            Encoding? encoding = null;

            if (document.Declaration?.Encoding is { Length: > 0 } name)
            {
                try
                {
                    encoding = Encoding.GetEncoding(name);
                }
                catch (ArgumentException)
                {
                    // an encoding this runtime does not know: the file was read as UTF-8 anyway
                }
            }

            encoding ??= Encoding.UTF8;

            // UTF-8 keeps the file's BOM state; other encodings come with their own preamble rules
            return encoding.CodePage == Encoding.UTF8.CodePage ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: hadBom) : encoding;
        }

        private static bool HasUtf8Bom(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);

                Span<byte> head = stackalloc byte[UTF8_BOM.Length];

                return stream.Read(head) == head.Length && head.SequenceEqual(UTF8_BOM);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Render(XDocument document, Encoding encoding, string newLine)
        {
            var settings = new XmlWriterSettings
            {
                Indent = false, // the document carries its own whitespace (PreserveWhitespace)
                NewLineHandling = NewLineHandling.Replace,
                NewLineChars = newLine,
                OmitXmlDeclaration = document.Declaration is null,
                Encoding = encoding,
                CloseOutput = false,
            };

            using var buffer = new MemoryStream();

            using (var writer = XmlWriter.Create(buffer, settings))
                document.Save(writer);

            return buffer.ToArray();
        }

        #endregion

        #region Replacing

        // write next to the file, then move over it, so a crash never leaves a half-written
        // configuration (§4.5). When the move is refused - Windows: another reader (a
        // configuration provider mid-reload, an editor) holds the file open, which shares
        // read/write but not delete/rename, reported as an access denied - the file is
        // rewritten in place instead: not atomic, but the window is kept small (the file is
        // never truncated first, and the complete new content sits in the .tmp file until the
        // rewrite is done, so a crash mid-way leaves it right next to the file)
        private static void Replace(string path, byte[] bytes, ILogger logger)
        {
            string tmp = path + TEMP_SUFFIX;

            try
            {
                File.WriteAllBytes(tmp, bytes);

                // the replacement must not loosen the file's permissions: a configuration can
                // hold credentials (a router password), and the temp file only has the umask's
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(tmp, File.GetUnixFileMode(path));

                try
                {
                    File.Move(tmp, path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, $"Replacing the configuration file '{path}' was refused; rewriting it in place.");

                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

                    stream.Write(bytes);
                    stream.SetLength(bytes.Length);
                    stream.Flush();
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp);
                }
                catch
                {
                    // best effort: a stray .tmp is harmless
                }
            }
        }

        #endregion
    }
}
