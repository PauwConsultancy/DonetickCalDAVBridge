using System.Xml.Linq;
using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Xml;
using DonetickCalDav.Configuration;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles CalDAV REPORT requests: calendar-query and calendar-multiget.
/// These are the primary mechanisms Apple Calendar uses to fetch VTODO data.
/// </summary>
public sealed class ReportHandler
{
    private readonly ChoreCache _cache;
    private readonly CalDavSettings _calDavSettings;
    private readonly ILogger<ReportHandler> _logger;

    public ReportHandler(ChoreCache cache, IOptions<AppSettings> settings, ILogger<ReportHandler> logger)
    {
        _cache = cache;
        _calDavSettings = settings.Value.CalDav;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var requestDoc = await DavXmlReader.ReadRequestBodyAsync(context);
        if (requestDoc == null)
        {
            _logger.LogWarning("REPORT received with empty or unparseable body");
            context.Response.StatusCode = 400;
            return;
        }

        if (DavXmlReader.IsCalendarMultiget(requestDoc))
        {
            await HandleMultiget(context, requestDoc);
            return;
        }

        if (DavXmlReader.IsCalendarQuery(requestDoc))
        {
            await HandleQuery(context, requestDoc);
            return;
        }

        _logger.LogWarning("REPORT received with unsupported type: {RootName}", requestDoc.Root?.Name);
        context.Response.StatusCode = 501;
    }

    /// <summary>
    /// calendar-query: returns all VTODO resources in the collection.
    /// Apple Calendar uses this for initial sync and periodic full refreshes.
    /// </summary>
    private async Task HandleQuery(HttpContext ctx, XDocument doc)
    {
        var requestedProps = DavXmlReader.GetRequestedProperties(doc);
        var writer = new DavXmlWriter();
        var user = _calDavSettings.Username;
        var chores = _cache.GetAllChores();

        _logger.LogDebug("calendar-query: returning {Count} resources", chores.Count);

        foreach (var cached in chores)
        {
            var href = $"/caldav/calendars/{user}/tasks/donetick-{cached.Chore.Id}.ics";
            writer.AddResponse(href, BuildResourceProps(requestedProps, cached));
        }

        await writer.WriteResponseAsync(ctx);
    }

    /// <summary>
    /// calendar-multiget: returns only the specifically requested resources.
    /// Apple Calendar uses this after detecting changed ETags to fetch updated data.
    /// </summary>
    private async Task HandleMultiget(HttpContext ctx, XDocument doc)
    {
        var hrefs = DavXmlReader.GetMultigetHrefs(doc);
        var requestedProps = DavXmlReader.GetRequestedProperties(doc);
        var writer = new DavXmlWriter();

        _logger.LogDebug("calendar-multiget: {Count} hrefs requested", hrefs.Count);

        foreach (var href in hrefs)
        {
            // Apple sometimes includes the collection href — skip it
            if (href.TrimEnd('/').EndsWith("/tasks", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = PathHelper.ExtractChoreId(href);
            if (id == null) continue;

            var cached = _cache.GetChore(id.Value);
            if (cached == null)
            {
                writer.AddResponse(href, new Dictionary<XName, object?>(), requestedProps);
                continue;
            }

            writer.AddResponse(href, BuildResourceProps(requestedProps, cached));
        }

        await writer.WriteResponseAsync(ctx);
    }

    /// <summary>Builds the property dictionary for a single VTODO resource.</summary>
    private static Dictionary<XName, object?> BuildResourceProps(List<XName> requested, CachedChore cached)
    {
        var props = new Dictionary<XName, object?>();

        foreach (var prop in requested)
        {
            if (prop == DavNamespaces.D + "getetag")
                props[prop] = cached.ETag;
            else if (prop == DavNamespaces.C + "calendar-data")
                props[prop] = VTodo.VTodoMapper.ToIcsString(cached.Chore);
            else if (prop == DavNamespaces.D + "getcontenttype")
                props[prop] = "text/calendar; component=vtodo";
        }

        return props;
    }
}
