using Microsoft.Extensions.Primitives;

namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// Interpreted metadata for an HTTP request. A capture parser creates this context only
    /// after the request line and complete header section are available; a body is not required.
    /// An adapter supplies metadata about the original request, not its authorization call.
    /// </summary>
    /// <remarks>
    /// Nullable properties use null when their original value is unknown or not present.
    /// A source unable to provide a required non-nullable property must throw
    /// <see cref="NotSupportedException"/>, rather than invent a value.
    /// Raw-payload access may be unsupported even when HTTP metadata is available.
    /// </remarks>
    public interface IHTTPFilterContext : IIPServicePayloadFilterContext
    {
        /// <summary>The original request method, preserving its case.</summary>
        public string Method { get; }

        /// <summary>
        /// The escaped path, without the query. Do not percent-decode it implicitly.
        /// An asterisk request target is represented as "*"; a target with no path has an empty path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// The raw query without a leading '?', without decoding or reordering.
        /// An absent or empty query is represented as an empty string.
        /// </summary>
        public string QueryString { get; }

        /// <summary>
        /// Decoded query parameters with ordinal, case-sensitive name lookup.
        /// Decode percent escapes once using UTF-8 and interpret '+' as a space.
        /// Preserve repeated-name values separately in their received order.
        /// A parameter without a value has an empty-string value; an absent or empty query
        /// yields an empty dictionary. The original representation remains in <see cref="QueryString"/>.
        /// </summary>
        /// 
        /// <exception cref="NotSupportedException">The source cannot provide the original query.</exception>
        public IReadOnlyDictionary<string, StringValues> Query { get; }

        /// <summary>
        /// Original protocol version, including the "HTTP/" prefix (for example, "HTTP/1.1"),
        /// or null when unavailable. Never substitute the authorization call's version.
        /// </summary>
        public string? Version { get; }

        /// <summary>The original URI scheme, or null when unavailable.</summary>
        public string? Scheme { get; }

        /// <summary>
        /// The original authority's hostname or IP address without a port.
        /// IPv6 addresses exclude the authority's enclosing brackets.
        /// </summary>
        public string? Host { get; }

        /// <summary>
        /// The original authority, retaining IPv6 brackets and any explicit port.
        /// This may differ from the watched backend service.
        /// </summary>
        public string? Authority { get; }

        /// <summary>
        /// The port explicitly specified in the original authority, or null when absent
        /// or unavailable. Do not substitute a scheme default or <see cref="IIPServiceFilterContext.Service"/>.
        /// </summary>
        public ushort? Port { get; }

        /// <summary>
        /// Original request headers with ordinal, case-insensitive name lookup.
        /// Preserve repeated field values as separate values. A missing key denotes an absent header.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// The source cannot provide the original header collection with reliable absence information.
        /// An incomplete forwarded subset must not silently stand in for the original collection.
        /// </exception>
        public IReadOnlyDictionary<string, StringValues> Headers { get; }

        /// <summary>
        /// Cookies with ordinal, case-sensitive name lookup. Preserve duplicate-name values
        /// separately in their received order; do not comma-join them. A missing key denotes absence.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// The source cannot provide the original cookie collection with reliable absence information.
        /// </exception>
        public IReadOnlyDictionary<string, StringValues> Cookies { get; }
    }
}
