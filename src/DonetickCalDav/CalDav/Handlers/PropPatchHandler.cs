using DonetickCalDav.CalDav.Xml;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Stub handler for PROPPATCH requests.
/// Apple Calendar sends these to set calendar-color, calendar-order, etc.
/// We accept the request but report all property changes as "forbidden" (404)
/// since collection properties are managed via configuration, not client writes.
/// </summary>
public sealed class PropPatchHandler
{
    private readonly ILogger<PropPatchHandler> _logger;

    public PropPatchHandler(ILogger<PropPatchHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var doc = await DavXmlReader.ReadRequestBodyAsync(context);
        if (doc == null)
        {
            context.Response.StatusCode = 207;
            return;
        }

        var path = context.Request.Path.Value ?? "/";

        // Collect all properties from both <set> and <remove> elements
        var setProps = doc.Descendants(DavNamespaces.D + "set")
            .SelectMany(s => s.Descendants(DavNamespaces.D + "prop"))
            .SelectMany(p => p.Elements())
            .Select(e => e.Name);

        var removeProps = doc.Descendants(DavNamespaces.D + "remove")
            .SelectMany(s => s.Descendants(DavNamespaces.D + "prop"))
            .SelectMany(p => p.Elements())
            .Select(e => e.Name);

        var allProps = setProps.Concat(removeProps).ToList();

        _logger.LogDebug("PROPPATCH: rejecting {Count} property changes on {Path}", allProps.Count, path);

        // Return 207 with 404 for every property (signals "not modifiable")
        var writer = new DavXmlWriter();
        writer.AddResponse(path, new Dictionary<System.Xml.Linq.XName, object?>(), allProps);
        await writer.WriteResponseAsync(context);
    }
}
