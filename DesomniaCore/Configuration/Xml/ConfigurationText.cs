using System.Text;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// Decodes the configuration file's bytes into text the way an XML parser would: a byte
    /// order mark wins, otherwise the <c>encoding</c> named in the XML declaration, and UTF-8
    /// when there is neither. Every reader of the file (the providers, the configuration
    /// pipeline, the migrator's own-write comparison) goes through here, so they all see the
    /// same text — and a file declared in a legacy encoding is not silently mangled (a
    /// UTF-8-only decode turns its non-ASCII characters into U+FFFD, which a file-mode
    /// migration would then write back).
    /// </summary>
    internal static partial class ConfigurationText
    {
        // the declaration must be the very first thing in the file, in ASCII-compatible bytes
        const int DECLARATION_PEEK = 256;

        [GeneratedRegex("""^<\?xml\s[^?]*?\bencoding\s*=\s*(?:"([^"]*)"|'([^']*)')""", RegexOptions.CultureInvariant)]
        private static partial Regex DeclaredEncoding();

        /// <summary>Reads and decodes the whole file.</summary>
        public static string ReadFile(string path) => Decode(File.ReadAllBytes(path));

        /// <summary>The newline style of the text: Windows when it contains one, else Unix.</summary>
        public static string DetectNewLine(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

        /// <summary>Reads the stream to its end and decodes it. Does not dispose the stream.</summary>
        public static string Decode(Stream stream)
        {
            using var buffer = new MemoryStream();

            stream.CopyTo(buffer);

            return Decode(buffer.ToArray());
        }

        /// <summary>Decodes the bytes: BOM first, then the declared encoding, then UTF-8.</summary>
        public static string Decode(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), SniffEncoding(bytes), detectEncodingFromByteOrderMarks: true);

            return reader.ReadToEnd();
        }

        // the encoding named in the declaration, when there is no BOM to say otherwise; a
        // name this runtime does not know falls back to UTF-8 (the writer does the same)
        private static Encoding SniffEncoding(byte[] bytes)
        {
            if (HasByteOrderMark(bytes))
                return Encoding.UTF8; // the reader detects and honours the mark

            string head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, DECLARATION_PEEK));

            if (DeclaredEncoding().Match(head) is { Success: true } match)
            {
                string name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

                try
                {
                    return Encoding.GetEncoding(name);
                }
                catch (ArgumentException)
                {
                    // unknown to this runtime: read as UTF-8, like before
                }
            }

            return Encoding.UTF8;
        }

        private static bool HasByteOrderMark(byte[] bytes)
            => bytes.Length >= 2 && (
                (bytes[0] == 0xFF && bytes[1] == 0xFE) ||    // UTF-16 LE (and UTF-32 LE)
                (bytes[0] == 0xFE && bytes[1] == 0xFF) ||    // UTF-16 BE
                (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));
    }
}
