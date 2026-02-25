using System.Xml.Linq;

namespace DonetickCalDav.CalDav.Xml;

/// <summary>
/// Builds WebDAV 207 Multi-Status XML responses.
/// Handles the two-tier propstat structure: found properties (200 OK)
/// and not-found properties (404 Not Found).
/// </summary>
public sealed class DavXmlWriter
{
    private readonly List<XElement> _responses = [];
    private string? _syncToken;

    /// <summary>
    /// Adds a resource response to the multistatus document.
    /// </summary>
    /// <param name="href">The resource URL (always include trailing slash for collections).</param>
    /// <param name="foundProps">Properties that exist, with their values.</param>
    /// <param name="notFoundProps">Properties that were requested but are not supported.</param>
    public void AddResponse(
        string href,
        Dictionary<XName, object?> foundProps,
        List<XName>? notFoundProps = null)
    {
        var response = new XElement(DavNamespaces.D + "response",
            new XElement(DavNamespaces.D + "href", href));

        AppendFoundProperties(response, foundProps);
        AppendNotFoundProperties(response, notFoundProps);

        _responses.Add(response);
    }

    /// <summary>
    /// Sets a sync-token to include in the multistatus response.
    /// Used by sync-collection REPORT responses.
    /// </summary>
    public void AddSyncToken(string syncToken) => _syncToken = syncToken;

    /// <summary>
    /// Writes the complete 207 Multi-Status XML response to the HTTP response stream.
    /// </summary>
    public async Task WriteResponseAsync(HttpContext context)
    {
        context.Response.StatusCode = 207;
        context.Response.ContentType = "application/xml; charset=utf-8";

        var multistatus = new XElement(DavNamespaces.D + "multistatus",
            new XAttribute(XNamespace.Xmlns + "d", DavNamespaces.DavUri),
            new XAttribute(XNamespace.Xmlns + "cal", DavNamespaces.CalDavUri),
            new XAttribute(XNamespace.Xmlns + "cs", DavNamespaces.CalendarServerUri),
            new XAttribute(XNamespace.Xmlns + "apple", DavNamespaces.AppleUri),
            _responses);

        if (_syncToken != null)
            multistatus.Add(new XElement(DavNamespaces.D + "sync-token", _syncToken));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), multistatus);

        await using var writer = new StreamWriter(context.Response.Body);
        await writer.WriteAsync(doc.Declaration + "\n" + doc);
    }

    /// <summary>Builds the 200 OK propstat section for found properties.</summary>
    private static void AppendFoundProperties(XElement response, Dictionary<XName, object?> foundProps)
    {
        if (foundProps.Count == 0) return;

        var propElement = new XElement(DavNamespaces.D + "prop");

        foreach (var (name, value) in foundProps)
        {
            propElement.Add(value switch
            {
                XElement element        => new XElement(name, element),
                IEnumerable<XElement> e => new XElement(name, e),
                string str              => new XElement(name, str),
                _                       => new XElement(name),
            });
        }

        response.Add(new XElement(DavNamespaces.D + "propstat",
            propElement,
            new XElement(DavNamespaces.D + "status", "HTTP/1.1 200 OK")));
    }

    /// <summary>Builds the 404 Not Found propstat section for unsupported properties.</summary>
    private static void AppendNotFoundProperties(XElement response, List<XName>? notFoundProps)
    {
        if (notFoundProps is not { Count: > 0 }) return;

        var propElement = new XElement(DavNamespaces.D + "prop");
        foreach (var name in notFoundProps)
            propElement.Add(new XElement(name));

        response.Add(new XElement(DavNamespaces.D + "propstat",
            propElement,
            new XElement(DavNamespaces.D + "status", "HTTP/1.1 404 Not Found")));
    }
}
