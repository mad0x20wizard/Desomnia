using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Filter;
using System.Net;
using System.Text;
using Xunit;
using static MadWizard.Desomnia.Network.Tests.FilterParserPackets;

namespace MadWizard.Desomnia.Network.Tests;

public class HTTPFilterContextParserTests
{
    private readonly HTTPFilterContextParser _parser = new();

    private IHTTPFilterContext Parse(string data)
    {
        var capture = Tcp(100, data);
        return Assert.IsAssignableFrom<IHTTPFilterContext>(_parser.Parse(ref capture, FlowDirection.Inbound));
    }

    [Fact]
    public void CompleteHeadersPreserveRawMetadataAndRepeatedValues()
    {
        const string request = "GET /a%2fb?x=one&x=two&X=three&encoded=a%26b%3Dc&flag&utf8=%C3%A4&plus=a+b&once=%252F HTTP/1.1\r\n"
            + "Host: Example.test:8080\r\nX-Tag: first\r\nCookie: id=one; ID=upper; quoted=\"value\"\r\n"
            + "x-tag: second\r\nCookie: id=two\r\nX-Empty:\r\n\r\n";
        var context = Parse(request);
        Assert.Equal("GET", context.Method);
        Assert.Equal("/a%2fb", context.Path);
        Assert.Equal("x=one&x=two&X=three&encoded=a%26b%3Dc&flag&utf8=%C3%A4&plus=a+b&once=%252F", context.QueryString);
        Assert.Equal(new[] { "one", "two" }, context.Query["x"].ToArray());
        Assert.Equal("three", context.Query["X"].ToString());
        Assert.Equal("a&b=c", context.Query["encoded"].ToString());
        Assert.Equal("", context.Query["flag"].ToString());
        Assert.Equal("ä", context.Query["utf8"].ToString());
        Assert.Equal("a b", context.Query["plus"].ToString());
        Assert.Equal("%2F", context.Query["once"].ToString());
        Assert.Equal(new[] { "first", "second" }, context.Headers["X-TAG"].ToArray());
        Assert.Equal("", context.Headers["x-empty"].ToString());
        Assert.Equal(new[] { "one", "two" }, context.Cookies["id"].ToArray());
        Assert.Equal("upper", context.Cookies["ID"].ToString());
        Assert.Equal("value", context.Cookies["quoted"].ToString());
        Assert.False(context.Cookies.ContainsKey("Id"));
        Assert.False(context.Headers.ContainsKey("Absent"));
        Assert.Equal("HTTP/1.1", context.Version);
        Assert.Null(context.Scheme); // Capture alone cannot know a proxy's original scheme.
        Assert.Equal("Example.test", context.Host);
        Assert.Equal("Example.test:8080", context.Authority);
        Assert.Equal((ushort)8080, context.Port);
        Assert.Equal(new IPService(IPProtocol.TCP, 80), context.Service);
        Assert.Equal(request, Encoding.Latin1.GetString(context.PayloadData));
        Assert.Same(context.PayloadData, context.PayloadData);
    }

    [Theory]
    [InlineData("GET / HTTP/1.0\r\n\r\n", "/", "", null, null, null)]
    [InlineData("OPTIONS * HTTP/1.1\r\nHost: example.test\r\n\r\n", "*", "", null, "example.test", null)]
    [InlineData("CONNECT example.test:443 HTTP/1.1\r\nHost: example.test\r\n\r\n", "", "", null, "example.test", 443)]
    [InlineData("GET http://Example.test:80/a/../b%2f?q=x HTTP/1.1\r\nHost: ignored.test\r\n\r\n", "/a/../b%2f", "q=x", "http", "Example.test", 80)]
    [InlineData("GET https://example.test?a=b HTTP/1.1\r\nHost: ignored.test\r\n\r\n", "", "a=b", "https", "example.test", null)]
    [InlineData("GET /? HTTP/1.1\r\nHost: [2001:db8::1]\r\n\r\n", "/", "", null, "2001:db8::1", null)]
    public void TargetFormsKeepExplicitPortAndEscapedPath(string request, string path, string query,
        string? scheme, string? host, int? port)
    {
        var context = Parse(request);
        Assert.Equal(path, context.Path);
        Assert.Equal(query, context.QueryString);
        Assert.Equal(scheme, context.Scheme);
        Assert.Equal(host, context.Host);
        Assert.Equal(port, context.Port is { } actual ? (int)actual : null);
        if (query == "")
            Assert.Empty(context.Query);
    }

    [Theory]
    [InlineData("Content-Length: 1000\r\n")]
    [InlineData("Transfer-Encoding: chunked\r\n")]
    public void HeaderCompletionDoesNotRequireOrParseBody(string framing)
    {
        string header = $"POST /submit HTTP/1.1\r\nHost: test\r\n{framing}\r\n";
        Assert.Equal("POST", Parse(header).Method);
        var withBody = Parse(header + "\0\rNOT-HTTP\r\n\r\n");
        Assert.Equal(header.Length + 14, withBody.PayloadLength);
    }

    [Fact]
    public void LibraryPreservesMethodCaseAndSupportsItsLargestContentLengthWithoutBody()
    {
        var context = Parse("CuStOm / HTTP/1.1\r\nHost: test\r\nContent-Length: 2147483647\r\n\r\n");
        Assert.Equal("CuStOm", context.Method);
        Assert.Equal("2147483647", context.Headers["Content-Length"].ToString());
    }

    [Theory]
    [InlineData("GET https://[2001:db8::1]/ HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("CONNECT [2001:db8::1]:443 HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("POST / HTTP/1.1\r\nHost: test\r\nContent-Length: 2147483648\r\n\r\n")]
    public void DocumentedLibraryLimitsAreTerminalEvenWithoutSyn(string request)
    {
        var capture = Tcp(100, request);
        Assert.Throws<MalformedServicePayloadException>(() => _parser.Parse(ref capture, FlowDirection.Inbound));
    }

    [Fact]
    public void EverySplitRecreatesAnEquivalentContextAfterMoreCaptures()
    {
        const string request = "GET /path?a=%C3%A4 HTTP/1.1\r\nHost: example.test\r\nCookie: id=one; id=two\r\n\r\n";
        for (int split = 1; split < request.Length; split++)
        {
            EthernetCapture[] history = [Tcp(100, syn: true), Tcp(101, request[..split]),
                Tcp((uint)(101 + split), request[split..])];
            var partial = _parser.Parse(history.AsSpan(0, 2), FlowDirection.Inbound);
            Assert.False(partial is IHTTPFilterContext);
            var complete = Assert.IsAssignableFrom<IHTTPFilterContext>(_parser.Parse(history, FlowDirection.Inbound));
            Assert.Equal("/path", complete.Path);
            Assert.Equal("ä", complete.Query["a"].ToString());
            Assert.Equal(new[] { "one", "two" }, complete.Cookies["id"].ToArray());
        }
    }

    [Fact]
    public void GapDoesNotCreateHttpContextAndRetransmissionsDoNotDuplicateFields()
    {
        const string first = "GET / HTTP/1.1\r\n";
        const string second = "Host: test\r\n";
        EthernetCapture[] history = [Tcp(100, syn: true), Tcp(101, first),
            Tcp((uint)(101 + first.Length + second.Length), "\r\n"),
            Tcp((uint)(101 + first.Length), second), Tcp((uint)(101 + first.Length), second)];
        Assert.False(_parser.Parse(history.AsSpan(0, 3), FlowDirection.Inbound) is IHTTPFilterContext);
        var complete = Assert.IsAssignableFrom<IHTTPFilterContext>(_parser.Parse(history, FlowDirection.Inbound));
        Assert.Single(complete.Headers["Host"]);
    }

    [Fact]
    public void MidstreamFragmentsResponsesAndUdpRetainDefaultContext()
    {
        var fragment = Tcp(100, "Host: test\r\n\r\n");
        Assert.False(_parser.Parse(ref fragment, FlowDirection.Inbound) is IHTTPFilterContext);
        var response = Tcp(100, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", outbound: true);
        Assert.False(_parser.Parse(ref response, FlowDirection.Outbound) is IHTTPFilterContext);
        var udp = Udp("GET / HTTP/1.1\r\nHost: test\r\n\r\n");
        Assert.False(_parser.Parse(ref udp, FlowDirection.Inbound) is IHTTPFilterContext);
        EthernetCapture[] history = [Tcp(100, "HTTP/1.1\r\nHost: test\r\n\r\n"), Tcp(94, "GET / ")];
        Assert.IsAssignableFrom<IHTTPFilterContext>(_parser.Parse(history, FlowDirection.Inbound));
    }

    [Theory]
    [InlineData("garbage\r\n\r\n")]
    [InlineData("GET / HTTP/2.0\r\nHost: test\r\n\r\n")]
    [InlineData("GET / HTTP/1.10\r\nHost: test\r\n\r\n")]
    [InlineData("GET / HTTP/01.1\r\nHost: test\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: one\r\nHost: two\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: bad:port\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: a@b\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost : test\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: test\r\n folded: value\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\nHost: test\n\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: test\rwrong\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: test\r\nX-Test: a\0b\r\n\r\n")]
    [InlineData("GET /%zz HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("GET /?x=%FF HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("GET /#fragment HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("GET * HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("CONNECT test HTTP/1.1\r\nHost: test\r\n\r\n")]
    [InlineData("POST / HTTP/1.1\r\nHost: test\r\nContent-Length: 1\r\nContent-Length: 2\r\n\r\n")]
    [InlineData("POST / HTTP/1.1\r\nHost: test\r\nContent-Length: -1\r\n\r\n")]
    [InlineData("POST / HTTP/1.1\r\nHost: test\r\nContent-Length: 99999999999999999999999\r\n\r\n")]
    [InlineData("POST / HTTP/1.1\r\nHost: test\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n\r\n")]
    public void EstablishedMalformedOrUnsupportedHttpIsTerminal(string request)
    {
        EthernetCapture[] history = [Tcp(100, syn: true), Tcp(101, request)];
        var error = Assert.Throws<MalformedServicePayloadException>(() => _parser.Parse(history, FlowDirection.Inbound));
        Assert.Equal(new IPService(IPProtocol.TCP, 80), error.Service);
    }

    private sealed class SmallHeadersParser : HTTPFilterContextParser
    {
        protected override int MaximumHttpHeaderLength => 64;
        protected override int MaximumHttpHeaderCount => 2;
    }

    [Fact]
    public void HeaderLimitsApplyBeforeCompletionButDoNotCountBody()
    {
        var parser = new SmallHeadersParser();
        var oversized = Tcp(100, "GET / HTTP/1.1\r\nHost: " + new string('x', 64), syn: true);
        Assert.Throws<MalformedServicePayloadException>(() => parser.Parse(ref oversized, FlowDirection.Inbound));
        var many = Tcp(100, "GET / HTTP/1.1\r\nHost: test\r\nX-One: 1\r\nX-Two: 2\r\n", syn: true);
        Assert.Throws<MalformedServicePayloadException>(() => parser.Parse(ref many, FlowDirection.Inbound));
        var body = Tcp(100, "GET / HTTP/1.1\r\nHost: test\r\n\r\n" + new string('x', 100));
        Assert.IsAssignableFrom<IHTTPFilterContext>(parser.Parse(ref body, FlowDirection.Inbound));
    }
}
