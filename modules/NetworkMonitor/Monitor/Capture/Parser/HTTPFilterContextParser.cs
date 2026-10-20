using HttpMachine;
using IHttpMachine;
using IHttpMachine.Model;
using MadWizard.Desomnia.Network.Filter;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>
    /// Interprets the first HTTP/1.0 or HTTP/1.1 request at the available TCP prefix.
    /// Requires complete headers, not a body. Does not scan arbitrary fragments for requests,
    /// decrypt TLS, parse responses, or track pipelined requests across evaluations.
    /// </summary>
    /// <remarks>
    /// Uses HttpMachine.PCL 6.2.0 for HTTP syntax. Its bracketed IPv6 request-target and
    /// signed 32-bit Content-Length restrictions are intentional initial limitations.
    /// IPv6 Host fields are supported. Unsupported input raises MalformedServicePayloadException.
    /// </remarks>
    public class HTTPFilterContextParser : DefaultFilterContextParser
    {
        /// <summary>Request line and header section together, including their CRLF delimiters.</summary>
        protected virtual int MaximumHttpHeaderLength => 64 * 1024;

        protected virtual int MaximumHttpHeaderCount => 100;

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        protected override IFilterContext Interpret(IFilterContext context, bool startsAtSyn)
        {
            if (context is not IIPServicePayloadFilterContext { Protocol: IPProtocol.TCP } payload)
                return context;
            if (MaximumHttpHeaderLength <= 0 || MaximumHttpHeaderCount <= 0)
                throw new InvalidOperationException("HTTP parsing limits must be positive.");

            ReadOnlySpan<byte> data = payload.PayloadData;
            int end = data.IndexOf("\r\n\r\n"u8);
            int headerLength = end < 0 ? data.Length : end + 4;
            if (headerLength > MaximumHttpHeaderLength)
                throw Invalid("HTTP request headers exceed the configured byte limit.");

            // Latin-1 preserves octets one-for-one; UTF-8 decoding is restricted to query values.
            string header = Encoding.Latin1.GetString(data[..headerLength]);
            int lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd < 0)
            {
                if (startsAtSyn)
                    ValidateLineEndings(header, Invalid);
                return context;
            }
            if (header.StartsWith("HTTP/", StringComparison.Ordinal))
                return context; // Response data is not an HTTP request context.

            // A parser instance lives for this evaluation only. Feed at most the header
            // section so body framing, trailers and subsequent requests cannot affect it.
            bool looksLikeRequest = header.AsSpan(0, lineEnd).Contains(" HTTP/", StringComparison.Ordinal);
            using var collector = new HeaderCollector(MaximumHttpHeaderCount, Invalid);
            using var parser = new HttpCombinedParser(collector);
            int consumed;
            try { consumed = parser.Execute(data[..headerLength]); }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new MalformedServicePayloadException(payload.Service,
                    "HTTP framing value is invalid or exceeds the parser's supported numeric range.", exception);
            }
            if (collector.Error || consumed != headerLength)
            {
                if (!startsAtSyn && !looksLikeRequest)
                    return context;
                throw Invalid("Invalid HTTP request line or header syntax.");
            }
            if (!collector.IsRequest)
                return context;
            ValidateLineEndings(header, Invalid);
            if (!collector.Complete)
                return context;
            if (parser.MajorVersion != 1 || parser.MinorVersion is not 0 and not 1)
                throw Invalid("Only HTTP/1.0 and HTTP/1.1 request headers are supported.");

            string version = $"HTTP/{parser.MajorVersion}.{parser.MinorVersion}";
            var headers = collector.Headers;
            headers.TryGetValue("Host", out var hosts);
            if (hosts.Count > 1 || version == "HTTP/1.1" && hosts.Count != 1)
                throw Invalid("HTTP/1.1 requires one Host field; duplicate Host fields are invalid.");
            if (hosts.Count == 1)
                ParseAuthority(hosts[0]!, Invalid); // Validate even when the target supplies authority.
            ValidateFraming(headers, Invalid);

            string method = collector.Method!, target = collector.Target!, path, queryString = "";
            ValidateTarget(target, Invalid);
            string? scheme = null, authority = hosts.Count == 1 ? hosts[0] : null;
            if (method == "CONNECT")
            {
                authority = target;
                path = "";
                if (ParseAuthority(authority, Invalid).Port is null)
                    throw Invalid("CONNECT requires an explicit target port.");
            }
            else if (target == "*")
            {
                if (method != "OPTIONS")
                    throw Invalid("Only OPTIONS supports an asterisk request target.");
                path = "*";
            }
            else
            {
                string pathAndQuery = target;
                if (!target.StartsWith('/'))
                {
                    int separator = target.IndexOf("://", StringComparison.Ordinal);
                    if (separator < 0 || !target[..separator].Equals("http", StringComparison.OrdinalIgnoreCase)
                        && !target[..separator].Equals("https", StringComparison.OrdinalIgnoreCase))
                        throw Invalid("Unsupported HTTP request-target form.");
                    scheme = target[..separator].ToLowerInvariant();
                    int authorityStart = separator + 3;
                    int authorityEnd = target.IndexOfAny(['/', '?'], authorityStart);
                    if (authorityEnd < 0)
                        authorityEnd = target.Length;
                    authority = target[authorityStart..authorityEnd];
                    if (authority.Length == 0)
                        throw Invalid("An absolute request target requires an authority.");
                    pathAndQuery = target[authorityEnd..];
                }
                int queryStart = pathAndQuery.IndexOf('?');
                path = queryStart < 0 ? pathAndQuery : pathAndQuery[..queryStart];
                if (queryStart >= 0)
                    queryString = pathAndQuery[(queryStart + 1)..];
            }

            var (host, port) = ParseAuthority(authority, Invalid);
            return new HTTPCaptureFilterContext(payload)
            {
                Direction = payload.Direction,
                Method = method,
                Path = path,
                QueryString = queryString,
                Version = version,
                Scheme = scheme,
                Authority = authority,
                Host = host,
                Port = port,
                Query = ParseQuery(queryString, Invalid),
                Headers = headers,
                Cookies = ParseCookies(headers, Invalid),
            };

            MalformedServicePayloadException Invalid(string message) => new(payload.Service, message);
        }

        private static bool IsToken(string value) => value.Length > 0 && value.All(c =>
            char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));

        // The library's standard delegate publishes only at message/body completion.
        // Collect header callbacks directly to avoid both that dependency and a body buffer.
        private sealed class HeaderCollector(int maximumCount, Func<string, Exception> invalid) : IHttpParserCombinedDelegate
        {
            private string _name = "";
            private int _count;
            public string? Method { get; private set; }
            public string? Target { get; private set; }
            public bool IsRequest { get; private set; }
            public bool Complete { get; private set; }
            public bool Error { get; private set; }
            public Dictionary<string, StringValues> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
            HttpRequestResponse IHttpParserDelegate.HttpRequestResponse => throw new NotSupportedException("Only request headers are collected.");

            public void OnHeaderName(IHttpCombinedParser parser, string name)
            {
                if (++_count > maximumCount)
                    throw invalid("HTTP request headers exceed the configured field limit.");
                if (!IsToken(name))
                    throw invalid("Invalid HTTP field name.");
                _name = name;
            }
            public void OnHeaderValue(IHttpCombinedParser parser, string value)
            {
                if (value.Any(c => c < 32 && c != '\t' || c == 127))
                    throw invalid("Invalid control character in HTTP field value.");
                Add(Headers, _name, value.Trim(' ', '\t'));
            }
            public void OnHeadersEnd(IHttpCombinedParser parser) => Complete = true;
            public void OnMethod(IHttpCombinedParser parser, string method) => Method = method;
            public void OnRequestUri(IHttpCombinedParser parser, string uri) => Target = uri;
            public void OnRequestType(IHttpCombinedParser parser) => IsRequest = true;
            public void OnParserError() => Error = true;
            public void OnMessageBegin(IHttpCombinedParser parser) { }
            public void OnMessageEnd(IHttpCombinedParser parser) { }
            public void OnResponseType(IHttpCombinedParser parser) { }
            public void OnResponseCode(IHttpCombinedParser parser, int code, string reason) { }
            public void OnPath(IHttpCombinedParser parser, string path) { }
            public void OnFragment(IHttpCombinedParser parser, string fragment) { }
            public void OnQueryString(IHttpCombinedParser parser, string query) { }
            public void OnTransferEncodingChunked(IHttpCombinedParser parser, bool chunked) { }
            public void OnChunkedLength(IHttpCombinedParser parser, int length) { }
            public void OnChunkReceived(IHttpCombinedParser parser) { }
            public void OnBody(IHttpCombinedParser parser, ArraySegment<byte> data)
                => throw new InvalidOperationException("The HTTP header parser must not receive body bytes.");
            public void Dispose() { }
        }

        private static void Add(Dictionary<string, StringValues> values, string name, string value)
        {
            values[name] = values.TryGetValue(name, out var previous)
                ? StringValues.Concat(previous, value) : new StringValues(value);
        }

        private static void ValidateLineEndings(string header, Func<string, Exception> invalid)
        {
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] == '\n' && (i == 0 || header[i - 1] != '\r')
                    || header[i] == '\r' && i + 1 < header.Length && header[i + 1] != '\n')
                    throw invalid("HTTP headers require CRLF line endings.");
                if (i > 0 && header[i - 1] == '\n' && header[i] is ' ' or '\t')
                    throw invalid("Obsolete folded HTTP headers are not supported.");
            }
        }

        private static void ValidateTarget(string target, Func<string, Exception> invalid)
        {
            for (int i = 0; i < target.Length; i++)
            {
                char c = target[i];
                if (c == '%')
                {
                    if (i + 2 >= target.Length || !Uri.IsHexDigit(target[i + 1]) || !Uri.IsHexDigit(target[i + 2]))
                        throw invalid("Invalid percent escape in HTTP request target.");
                    i += 2;
                }
                else if (!char.IsAsciiLetterOrDigit(c) && !"-._~:/?[]@!$&'()*+,;=".Contains(c))
                    throw invalid("Invalid character in HTTP request target.");
            }
        }

        private static (string? Host, ushort? Port) ParseAuthority(string? authority, Func<string, Exception> invalid)
        {
            if (string.IsNullOrEmpty(authority))
                return (null, null);
            string host, portText;
            if (authority[0] == '[')
            {
                int close = authority.IndexOf(']');
                if (close < 0)
                    throw invalid("Invalid bracketed IPv6 authority.");
                host = authority[1..close];
                if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6
                    || host.Contains('%'))
                    throw invalid("Invalid IPv6 authority.");
                portText = authority[(close + 1)..];
            }
            else
            {
                int colon = authority.IndexOf(':');
                host = colon < 0 ? authority : authority[..colon];
                portText = colon < 0 ? "" : authority[colon..];
                if (host.Length == 0 || host.Any(c => !char.IsAsciiLetterOrDigit(c)
                    && !"-._~!$&'()*+;=".Contains(c)))
                    throw invalid("Invalid HTTP authority hostname.");
            }
            if (portText.Length == 0)
                return (host, null);
            if (portText[0] != ':' || portText.Length == 1
                || !ushort.TryParse(portText.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                throw invalid("Invalid HTTP authority port.");
            return (host, port);
        }

        private static void ValidateFraming(Dictionary<string, StringValues> headers, Func<string, Exception> invalid)
        {
            if (!headers.TryGetValue("Content-Length", out var lengths))
                return;
            if (headers.ContainsKey("Transfer-Encoding"))
                throw invalid("Conflicting HTTP Transfer-Encoding and Content-Length fields.");
            ulong? expected = null;
            foreach (string? field in lengths)
                foreach (string item in field!.Split(','))
                {
                    if (!ulong.TryParse(item.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)
                        || expected is { } previous && previous != value)
                        throw invalid("Invalid or conflicting HTTP Content-Length fields.");
                    expected = value;
                }
        }

        private static Dictionary<string, StringValues> ParseQuery(string query, Func<string, Exception> invalid)
        {
            var values = new Dictionary<string, StringValues>(StringComparer.Ordinal);
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                string name = equals < 0 ? pair : pair[..equals];
                string value = equals < 0 ? "" : pair[(equals + 1)..];
                Add(values, Decode(name), Decode(value));
            }
            return values;

            string Decode(string value)
            {
                byte[] bytes = new byte[value.Length];
                int length = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '%')
                    {
                        bytes[length++] = (byte)((Hex(value[++i]) << 4) | Hex(value[++i]));
                    }
                    else
                        bytes[length++] = c == '+' ? (byte)' ' : (byte)c;
                }
                try { return StrictUtf8.GetString(bytes, 0, length); }
                catch (DecoderFallbackException) { throw invalid("Query parameters contain invalid UTF-8."); }
            }
            static int Hex(char c) => c <= '9' ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
        }

        private static Dictionary<string, StringValues> ParseCookies(
            Dictionary<string, StringValues> headers, Func<string, Exception> invalid)
        {
            var cookies = new Dictionary<string, StringValues>(StringComparer.Ordinal);
            if (headers.TryGetValue("Cookie", out var fields))
                foreach (string? field in fields)
                    foreach (string part in field!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string pair = part.Trim(' ', '\t');
                        int equals = pair.IndexOf('=');
                        if (equals <= 0 || !IsToken(pair[..equals]))
                            throw invalid("Invalid HTTP cookie pair.");
                        string value = pair[(equals + 1)..];
                        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                            value = value[1..^1];
                        if (value.Any(c => c < 0x21 || c > 0x7e || c is '"' or ',' or ';' or '\\'))
                            throw invalid("Invalid HTTP cookie value.");
                        Add(cookies, pair[..equals], value);
                    }
            return cookies;
        }
    }
}
