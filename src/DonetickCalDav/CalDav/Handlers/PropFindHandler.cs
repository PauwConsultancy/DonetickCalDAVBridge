using System.Xml.Linq;
using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Xml;
using DonetickCalDav.Configuration;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles PROPFIND requests at every level of the CalDAV URL hierarchy.
/// Apple Calendar's discovery protocol requires correct responses at each depth:
///   /caldav/                              → service root (current-user-principal)
///   /caldav/principals/{user}/            → principal (calendar-home-set)
///   /caldav/calendars/{user}/             → calendar home (lists collections)
///   /caldav/calendars/{user}/{slug}/      → calendar collection (VTODO metadata)
///   /caldav/calendars/{user}/{slug}/*.ics → individual resource
/// </summary>
public sealed class PropFindHandler
{
    private readonly ChoreCache _cache;
    private readonly CalendarResolver _resolver;
    private readonly CalDavSettings _calDavSettings;
    private readonly ILogger<PropFindHandler> _logger;

    public PropFindHandler(
        ChoreCache cache,
        CalendarResolver resolver,
        IOptions<AppSettings> settings,
        ILogger<PropFindHandler> logger)
    {
        _cache = cache;
        _resolver = resolver;
        _calDavSettings = settings.Value.CalDav;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var path = (context.Request.Path.Value ?? "/").TrimEnd('/');
        var depth = context.Request.Headers["Depth"].FirstOrDefault() ?? "0";
        var requestDoc = await DavXmlReader.ReadRequestBodyAsync(context);
        var requestedProps = requestDoc != null ? DavXmlReader.GetRequestedProperties(requestDoc) : [];
        var user = _calDavSettings.Username;

        _logger.LogDebug("PROPFIND {Path} Depth:{Depth} Props:{Count}", path, depth, requestedProps.Count);

        // Route to the correct handler based on URL depth — early returns keep it flat
        if (path.Equals("/caldav", StringComparison.OrdinalIgnoreCase))
        {
            await RespondServiceRoot(context, requestedProps, user);
            return;
        }

        if (path.Equals($"/caldav/principals/{user}", StringComparison.OrdinalIgnoreCase))
        {
            await RespondPrincipal(context, requestedProps, user);
            return;
        }

        if (path.Equals($"/caldav/calendars/{user}", StringComparison.OrdinalIgnoreCase))
        {
            await RespondCalendarHome(context, requestedProps, user, depth);
            return;
        }

        // Calendar collection: match any valid slug (e.g. /tasks, /werk, /dagelijks)
        var slug = PathHelper.ExtractCalendarSlug(path, user);
        if (slug != null && _resolver.IsValidCalendar(slug) &&
            path.Equals($"/caldav/calendars/{user}/{slug}", StringComparison.OrdinalIgnoreCase))
        {
            await RespondCalendarCollection(context, requestedProps, user, depth, slug);
            return;
        }

        // Individual resource PROPFIND
        var choreId = PathHelper.ExtractChoreId(path);
        if (choreId.HasValue)
        {
            await RespondResource(context, requestedProps, user, choreId.Value);
            return;
        }

        _logger.LogDebug("PROPFIND 404: no matching path for {Path}", path);
        context.Response.StatusCode = 404;
    }

    // ── Level 1: Service Root ──────────────────────────────────────────────────

    private async Task RespondServiceRoot(HttpContext ctx, List<XName> props, string user)
    {
        var writer = new DavXmlWriter();
        var (found, notFound) = ResolveProperties(props, name => name switch
        {
            _ when name == DavNamespaces.D + "current-user-principal" => Href($"/caldav/principals/{user}/"),
            _ when name == DavNamespaces.D + "principal-URL"          => Href($"/caldav/principals/{user}/"),
            _ when name == DavNamespaces.D + "resourcetype"           => new XElement(DavNamespaces.D + "collection"),
            _ => null,
        });

        // Allprop fallback when no properties were explicitly requested
        if (props.Count == 0)
        {
            found[DavNamespaces.D + "current-user-principal"] = Href($"/caldav/principals/{user}/");
            found[DavNamespaces.D + "resourcetype"] = new XElement(DavNamespaces.D + "collection");
        }

        writer.AddResponse("/caldav/", found, NullIfEmpty(notFound));
        await writer.WriteResponseAsync(ctx);
    }

    // ── Level 2: Principal ─────────────────────────────────────────────────────

    private async Task RespondPrincipal(HttpContext ctx, List<XName> props, string user)
    {
        var writer = new DavXmlWriter();
        var (found, notFound) = ResolveProperties(props, name => name switch
        {
            _ when name == DavNamespaces.C + "calendar-home-set"       => Href($"/caldav/calendars/{user}/"),
            _ when name == DavNamespaces.D + "current-user-principal"  => Href($"/caldav/principals/{user}/"),
            _ when name == DavNamespaces.D + "principal-URL"           => Href($"/caldav/principals/{user}/"),
            _ when name == DavNamespaces.D + "resourcetype"
                => (object)new XElement[] { new(DavNamespaces.D + "collection"), new(DavNamespaces.D + "principal") },
            _ when name == DavNamespaces.D + "displayname"             => user,
            _ when name == DavNamespaces.D + "supported-report-set"    => SupportedReportSet(),
            _ => null,
        });

        if (props.Count == 0)
        {
            found[DavNamespaces.C + "calendar-home-set"] = Href($"/caldav/calendars/{user}/");
            found[DavNamespaces.D + "resourcetype"] = (object)new XElement[]
                { new(DavNamespaces.D + "collection"), new(DavNamespaces.D + "principal") };
        }

        writer.AddResponse($"/caldav/principals/{user}/", found, NullIfEmpty(notFound));
        await writer.WriteResponseAsync(ctx);
    }

    // ── Level 3: Calendar Home ─────────────────────────────────────────────────

    private async Task RespondCalendarHome(HttpContext ctx, List<XName> props, string user, string depth)
    {
        var writer = new DavXmlWriter();
        var basePath = $"/caldav/calendars/{user}/";

        var (homeFound, homeNotFound) = ResolveProperties(props, name => name switch
        {
            _ when name == DavNamespaces.D + "resourcetype"           => new XElement(DavNamespaces.D + "collection"),
            _ when name == DavNamespaces.D + "displayname"            => $"{user} Calendars",
            _ when name == DavNamespaces.D + "current-user-principal" => Href($"/caldav/principals/{user}/"),
            _ => null,
        });

        writer.AddResponse(basePath, homeFound, NullIfEmpty(homeNotFound));

        // Depth:1 — list all calendar collections (dynamic when GroupByLabel is enabled)
        if (depth == "1")
        {
            var calendars = _resolver.GetCalendars();
            for (var i = 0; i < calendars.Count; i++)
            {
                var cal = calendars[i];
                var (collFound, collNotFound) = BuildCollectionProperties(props, user, cal, i + 1);
                writer.AddResponse($"/caldav/calendars/{user}/{cal.Slug}/", collFound, NullIfEmpty(collNotFound));
            }
        }

        await writer.WriteResponseAsync(ctx);
    }

    // ── Level 4: Calendar Collection ───────────────────────────────────────────

    private async Task RespondCalendarCollection(
        HttpContext ctx, List<XName> props, string user, string depth, string slug)
    {
        var writer = new DavXmlWriter();

        // Find the virtual calendar for this slug (needed for display name, color, ctag)
        var calendars = _resolver.GetCalendars();
        var calIndex = calendars.FindIndex(c => c.Slug == slug);
        var cal = calIndex >= 0 ? calendars[calIndex] : null;

        if (cal == null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        var (collFound, collNotFound) = BuildCollectionProperties(props, user, cal, calIndex + 1);
        writer.AddResponse($"/caldav/calendars/{user}/{slug}/", collFound, NullIfEmpty(collNotFound));

        // Depth:1 — enumerate individual VTODO resources in this calendar
        if (depth == "1")
        {
            var chores = _resolver.GetChoresForCalendar(slug);
            foreach (var cached in chores)
            {
                var href = $"/caldav/calendars/{user}/{slug}/donetick-{cached.Chore.Id}.ics";
                writer.AddResponse(href, BuildResourceProperties(props, cached));
            }
        }

        await writer.WriteResponseAsync(ctx);
    }

    // ── Individual Resource ────────────────────────────────────────────────────

    private async Task RespondResource(HttpContext ctx, List<XName> props, string user, int choreId)
    {
        var cached = _cache.GetChore(choreId);
        if (cached == null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        // Determine which calendar this chore belongs to
        var slug = _resolver.GroupByLabel
            ? CalendarResolver.GetSlugForChore(cached.Chore)
            : CalendarResolver.DefaultSlug;

        var writer = new DavXmlWriter();
        writer.AddResponse(
            $"/caldav/calendars/{user}/{slug}/donetick-{choreId}.ics",
            BuildResourceProperties(props, cached));
        await writer.WriteResponseAsync(ctx);
    }

    // ── Property Builders ──────────────────────────────────────────────────────

    /// <summary>Builds the full set of properties for a calendar collection.</summary>
    private (Dictionary<XName, object?> Found, List<XName> NotFound) BuildCollectionProperties(
        List<XName> props, string user, VirtualCalendar cal, int order)
    {
        return ResolveProperties(props, name => name switch
        {
            _ when name == DavNamespaces.D + "resourcetype"
                => (object)new XElement[] { new(DavNamespaces.D + "collection"), new(DavNamespaces.C + "calendar") },
            _ when name == DavNamespaces.D + "displayname"
                => cal.DisplayName,
            _ when name == DavNamespaces.C + "supported-calendar-component-set"
                => new XElement(DavNamespaces.C + "comp", new XAttribute("name", "VTODO")),
            _ when name == DavNamespaces.CS + "getctag"
                => cal.CTag,
            _ when name == DavNamespaces.D + "sync-token"
                => $"http://donetick-caldav/sync/{cal.CTag}",
            _ when name == DavNamespaces.D + "current-user-privilege-set"
                => (object)new XElement[]
                {
                    new(DavNamespaces.D + "privilege", new XElement(DavNamespaces.D + "read")),
                    new(DavNamespaces.D + "privilege", new XElement(DavNamespaces.D + "write")),
                    new(DavNamespaces.D + "privilege", new XElement(DavNamespaces.D + "read-current-user-privilege-set")),
                },
            _ when name == DavNamespaces.Apple + "calendar-color"     => cal.Color,
            _ when name == DavNamespaces.Apple + "calendar-order"     => order.ToString(),
            _ when name == DavNamespaces.C + "calendar-description"   => "Tasks from Donetick",
            _ when name == DavNamespaces.D + "supported-report-set"   => SupportedReportSet(),
            _ when name == DavNamespaces.D + "current-user-principal" => Href($"/caldav/principals/{user}/"),
            _ when name == DavNamespaces.D + "owner"                  => Href($"/caldav/principals/{user}/"),
            _ => null,
        });
    }

    /// <summary>Builds properties for an individual VTODO resource.</summary>
    private Dictionary<XName, object?> BuildResourceProperties(List<XName> props, CachedChore cached)
    {
        var found = new Dictionary<XName, object?>();

        foreach (var prop in props)
        {
            if (prop == DavNamespaces.D + "getetag")
                found[prop] = cached.ETag;
            else if (prop == DavNamespaces.D + "getcontenttype")
                found[prop] = "text/calendar; component=vtodo";
            else if (prop == DavNamespaces.C + "calendar-data")
                found[prop] = VTodo.VTodoMapper.ToIcsString(cached.Chore, _calDavSettings.AllDayEvents);
            else if (prop == DavNamespaces.D + "resourcetype")
                found[prop] = null; // Empty resourcetype signals "not a collection"
        }

        return found;
    }

    // ── Shared Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates requested properties, splitting into found/not-found via a resolver function.
    /// </summary>
    private static (Dictionary<XName, object?> Found, List<XName> NotFound) ResolveProperties(
        List<XName> requestedProps, Func<XName, object?> resolver)
    {
        var found = new Dictionary<XName, object?>();
        var notFound = new List<XName>();

        foreach (var prop in requestedProps)
        {
            var value = resolver(prop);
            if (value != null)
                found[prop] = value;
            else
                notFound.Add(prop);
        }

        return (found, notFound);
    }

    private static XElement Href(string url) => new(DavNamespaces.D + "href", url);

    private static object SupportedReportSet() => (object)new XElement[]
    {
        new(DavNamespaces.D + "supported-report",
            new XElement(DavNamespaces.D + "report", new XElement(DavNamespaces.C + "calendar-query"))),
        new(DavNamespaces.D + "supported-report",
            new XElement(DavNamespaces.D + "report", new XElement(DavNamespaces.C + "calendar-multiget"))),
    };

    private static List<XName>? NullIfEmpty(List<XName> list) => list.Count > 0 ? list : null;
}
