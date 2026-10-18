using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// Minimal CalDAV client for tests: sends WebDAV methods with Basic auth and returns the whole
/// response as a <see cref="DavResponse"/>. Redirects are not followed, so discovery can be asserted.
/// </summary>
public sealed class CalDavClient
{
    /// <summary>User-Agent of Apple Reminders on macOS; the bridge uses it for client tracking only.</summary>
    public const string DefaultUserAgent = "Mac+OS+X/26.0 (25A354) remindd/1.0";

    private readonly HttpClient _http;
    private readonly AuthenticationHeaderValue? _auth;

    public CalDavClient(HttpClient http, string username, string password, string userAgent = DefaultUserAgent)
        : this(http, BasicAuth(username, password), userAgent)
    {
    }

    private CalDavClient(HttpClient http, AuthenticationHeaderValue? auth, string userAgent)
    {
        _http = http;
        _auth = auth;
        UserAgent = userAgent;
    }

    public string UserAgent { get; }

    /// <summary>A client on the same server that sends no credentials.</summary>
    public CalDavClient Anonymous() => new(_http, (AuthenticationHeaderValue?)null, UserAgent);

    /// <summary>A client on the same server with other credentials.</summary>
    public CalDavClient WithCredentials(string username, string password) =>
        new(_http, BasicAuth(username, password), UserAgent);

    /// <summary>A client on the same server with a raw Authorization header, for malformed or other schemes.</summary>
    public CalDavClient WithAuthorization(string scheme, string parameter) =>
        new(_http, new AuthenticationHeaderValue(scheme, parameter), UserAgent);

    /// <summary>A client on the same server with another User-Agent.</summary>
    public CalDavClient WithUserAgent(string userAgent) => new(_http, _auth, userAgent);

    public Task<DavResponse> OptionsAsync(string path) => SendAsync(HttpMethod.Options, path);

    public Task<DavResponse> PropFindAsync(string path, int depth, params XName[] properties) =>
        SendAsync(new HttpMethod("PROPFIND"), path, Dav.PropFind(properties), "application/xml", depth);

    public Task<DavResponse> ReportAsync(string path, string body, int depth = 1) =>
        SendAsync(new HttpMethod("REPORT"), path, body, "application/xml", depth);

    public Task<DavResponse> GetAsync(string path, string? ifNoneMatch = null) =>
        SendAsync(HttpMethod.Get, path, configure: r =>
        {
            if (ifNoneMatch != null) r.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        });

    public Task<DavResponse> HeadAsync(string path) => SendAsync(HttpMethod.Head, path);

    public Task<DavResponse> PutAsync(string path, string ics) =>
        SendAsync(HttpMethod.Put, path, ics, "text/calendar");

    public Task<DavResponse> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    /// <summary>Sends any method. <paramref name="depth"/> adds a Depth header.</summary>
    public async Task<DavResponse> SendAsync(
        HttpMethod method,
        string path,
        string? body = null,
        string? contentType = null,
        int? depth = null,
        Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = _auth;
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (depth is { } d)
            request.Headers.TryAddWithoutValidation("Depth", d.ToString());
        if (body != null)
        {
            // Parsed as a whole, so parameters such as charset="utf-8" from recorded sessions survive.
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/xml; charset=utf-8");
        }
        configure?.Invoke(request);

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new DavResponse(response.StatusCode, Flatten(response), text);
    }

    public static AuthenticationHeaderValue BasicAuth(string username, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

    private static Dictionary<string, string> Flatten(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
}

/// <summary>A buffered HTTP response with helpers for WebDAV multistatus bodies.</summary>
public sealed class DavResponse(HttpStatusCode status, IReadOnlyDictionary<string, string> headers, string body)
{
    public HttpStatusCode Status { get; } = status;

    public IReadOnlyDictionary<string, string> Headers { get; } = headers;

    public string Body { get; } = body;

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public string? ETag => Header("ETag");

    /// <summary>The body parsed as XML. Throws when the body is not XML.</summary>
    public XDocument Xml => XDocument.Parse(Body);

    /// <summary>All <c>d:response</c> elements of a multistatus body.</summary>
    public IReadOnlyList<XElement> Responses => Xml.Descendants(Dav.D + "response").ToList();

    /// <summary>The hrefs of all <c>d:response</c> elements, in document order.</summary>
    public IReadOnlyList<string> Hrefs =>
        Responses.Select(r => r.Element(Dav.D + "href")?.Value ?? "").ToList();

    /// <summary>The <c>d:response</c> for <paramref name="href"/>, or null.</summary>
    public XElement? ResponseFor(string href) =>
        Responses.FirstOrDefault(r => r.Element(Dav.D + "href")?.Value == href);

    /// <summary>
    /// A property of the response for <paramref name="href"/> that came back with status 200, or null
    /// when it is missing or was reported as not found.
    /// </summary>
    public XElement? Property(string href, XName property) =>
        PropStat(href, property, "200");

    /// <summary>True when <paramref name="property"/> is in a 404 propstat of <paramref name="href"/>.</summary>
    public bool IsNotFound(string href, XName property) => PropStat(href, property, "404") != null;

    private XElement? PropStat(string href, XName property, string statusCode) =>
        ResponseFor(href)?
            .Elements(Dav.D + "propstat")
            .Where(ps => ps.Element(Dav.D + "status")?.Value.Contains($" {statusCode} ") == true)
            .Select(ps => ps.Element(Dav.D + "prop")?.Element(property))
            .FirstOrDefault(p => p != null);

    /// <summary>The <c>calendar-data</c> (ICS text) of the response for <paramref name="href"/>, or null.</summary>
    public string? CalendarData(string href) => Property(href, Dav.C + "calendar-data")?.Value;

    /// <summary>The <c>getetag</c> of the response for <paramref name="href"/>, or null.</summary>
    public string? GetETag(string href) => Property(href, Dav.D + "getetag")?.Value;

    /// <summary>
    /// Text form for snapshots: status line, the named headers in the given order, and the body
    /// (XML re-indented). Volatile values are left to the caller's scrubbers.
    /// </summary>
    public string ToSnapshot(params string[] headerNames)
    {
        var text = new System.Text.StringBuilder($"HTTP {(int)Status}\n");
        foreach (var name in headerNames)
        {
            if (Header(name) is { } value)
                text.Append($"{name}: {value}\n");
        }

        text.Append('\n');
        text.Append(LooksLikeXml(Body) ? Snapshots.Scrubbers.Xml(Body) : Body);
        return text.ToString();
    }

    private static bool LooksLikeXml(string body) => body.TrimStart().StartsWith('<') && !body.TrimStart().StartsWith("<!DOCTYPE");

    public override string ToString() => $"{(int)Status} {Status}\n{Body}";
}
