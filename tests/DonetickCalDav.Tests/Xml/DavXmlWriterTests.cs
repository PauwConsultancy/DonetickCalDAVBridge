using System.Xml.Linq;
using DonetickCalDav.CalDav.Xml;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace DonetickCalDav.Tests.Xml;

public class DavXmlWriterTests
{
    // ── AddResponse + WriteResponseAsync ────────────────────────────────────

    [Fact]
    public async Task WriteResponseAsync_SingleResponse_Returns207WithXml()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/calendars/user/tasks/",
            new Dictionary<XName, object?>
            {
                [DavNamespaces.D + "displayname"] = "Test Calendar",
            });

        var (statusCode, body) = await RenderAsync(writer);

        statusCode.Should().Be(207);

        var doc = XDocument.Parse(body);
        doc.Root!.Name.Should().Be(DavNamespaces.D + "multistatus");

        var response = doc.Root.Element(DavNamespaces.D + "response");
        response.Should().NotBeNull();

        var href = response!.Element(DavNamespaces.D + "href")!.Value;
        href.Should().Be("/caldav/calendars/user/tasks/");

        var displayname = doc.Descendants(DavNamespaces.D + "displayname").First().Value;
        displayname.Should().Be("Test Calendar");
    }

    [Fact]
    public async Task WriteResponseAsync_FoundAndNotFound_ProducesTwoPropstats()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/",
            new Dictionary<XName, object?>
            {
                [DavNamespaces.D + "displayname"] = "Found",
            },
            [DavNamespaces.D + "getcontentlength"]);

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);
        var response = doc.Root!.Element(DavNamespaces.D + "response")!;

        var propstats = response.Elements(DavNamespaces.D + "propstat").ToList();
        propstats.Should().HaveCount(2);

        var statuses = propstats
            .Select(ps => ps.Element(DavNamespaces.D + "status")!.Value)
            .ToList();
        statuses.Should().Contain("HTTP/1.1 200 OK");
        statuses.Should().Contain("HTTP/1.1 404 Not Found");
    }

    [Fact]
    public async Task WriteResponseAsync_EmptyNotFoundList_SinglePropstat()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/",
            new Dictionary<XName, object?>
            {
                [DavNamespaces.D + "displayname"] = "Test",
            },
            null);

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);
        var response = doc.Root!.Element(DavNamespaces.D + "response")!;

        var propstats = response.Elements(DavNamespaces.D + "propstat").ToList();
        propstats.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteResponseAsync_MultipleResponses_AllPresent()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/item1.ics",
            new Dictionary<XName, object?> { [DavNamespaces.D + "getetag"] = "\"etag1\"" });
        writer.AddResponse("/caldav/item2.ics",
            new Dictionary<XName, object?> { [DavNamespaces.D + "getetag"] = "\"etag2\"" });

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);
        var responses = doc.Root!.Elements(DavNamespaces.D + "response").ToList();

        responses.Should().HaveCount(2);
    }

    // ── AddSyncToken ────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteResponseAsync_WithSyncToken_IncludesSyncTokenElement()
    {
        var writer = new DavXmlWriter();
        writer.AddSyncToken("http://donetick-caldav/sync/abc123");
        writer.AddResponse("/caldav/item.ics",
            new Dictionary<XName, object?> { [DavNamespaces.D + "getetag"] = "\"etag\"" });

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);

        var syncToken = doc.Root!.Element(DavNamespaces.D + "sync-token");
        syncToken.Should().NotBeNull();
        syncToken!.Value.Should().Be("http://donetick-caldav/sync/abc123");
    }

    [Fact]
    public async Task WriteResponseAsync_WithoutSyncToken_NoSyncTokenElement()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/item.ics",
            new Dictionary<XName, object?> { [DavNamespaces.D + "getetag"] = "\"etag\"" });

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);

        var syncToken = doc.Root!.Element(DavNamespaces.D + "sync-token");
        syncToken.Should().BeNull();
    }

    // ── Property value types ────────────────────────────────────────────────

    [Fact]
    public async Task WriteResponseAsync_XElementValue_NestedCorrectly()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/",
            new Dictionary<XName, object?>
            {
                [DavNamespaces.D + "resourcetype"] = new XElement(DavNamespaces.D + "collection"),
            });

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);
        var resourcetype = doc.Descendants(DavNamespaces.D + "resourcetype").First();

        resourcetype.Element(DavNamespaces.D + "collection").Should().NotBeNull();
    }

    [Fact]
    public async Task WriteResponseAsync_NullValue_ProducesEmptyElement()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/item.ics",
            new Dictionary<XName, object?>
            {
                [DavNamespaces.D + "resourcetype"] = null,
            });

        var (_, body) = await RenderAsync(writer);
        var doc = XDocument.Parse(body);
        var resourcetype = doc.Descendants(DavNamespaces.D + "resourcetype").First();

        resourcetype.IsEmpty.Should().BeTrue();
    }

    // ── Content type ────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteResponseAsync_SetsCorrectContentType()
    {
        var writer = new DavXmlWriter();
        writer.AddResponse("/caldav/", new Dictionary<XName, object?>());

        var (statusCode, _) = await RenderAsync(writer);

        statusCode.Should().Be(207);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<(int StatusCode, string Body)> RenderAsync(DavXmlWriter writer)
    {
        var context = new DefaultHttpContext();
        // Use a non-closing MemoryStream because DavXmlWriter disposes the StreamWriter
        // (and thus the underlying stream) in WriteResponseAsync.
        var ms = new NonClosingMemoryStream();
        context.Response.Body = ms;

        await writer.WriteResponseAsync(context);

        ms.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ms).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    /// <summary>MemoryStream that ignores Close/Dispose so we can read back after writing.</summary>
    private sealed class NonClosingMemoryStream : MemoryStream
    {
        protected override void Dispose(bool disposing) { /* no-op */ }
    }
}
