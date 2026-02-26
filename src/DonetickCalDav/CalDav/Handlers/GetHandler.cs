using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Configuration;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles GET requests for individual .ics (VTODO) resources.
/// Returns the full iCalendar content with appropriate ETag header.
/// Supports conditional requests via If-None-Match (returns 304 when ETag matches).
/// </summary>
public sealed class GetHandler
{
    private readonly ChoreCache _cache;
    private readonly CalDavSettings _calDavSettings;
    private readonly ILogger<GetHandler> _logger;

    public GetHandler(ChoreCache cache, IOptions<AppSettings> settings, ILogger<GetHandler> logger)
    {
        _cache = cache;
        _calDavSettings = settings.Value.CalDav;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var id = PathHelper.ResolveChoreIdFromPath(context.Request.Path.Value ?? "", _cache);
        if (id == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var cached = _cache.GetChore(id.Value);
        if (cached == null)
        {
            _logger.LogDebug("GET: chore {Id} not found in cache", id.Value);
            context.Response.StatusCode = 404;
            return;
        }

        // Conditional GET: return 304 Not Modified when client already has current version
        var ifNoneMatch = context.Request.Headers["If-None-Match"].FirstOrDefault();
        if (ifNoneMatch != null && ifNoneMatch == cached.ETag)
        {
            _logger.LogDebug("GET: chore {Id} — 304 Not Modified (ETag match)", id.Value);
            context.Response.StatusCode = 304;
            context.Response.Headers["ETag"] = cached.ETag;
            return;
        }

        _logger.LogDebug("GET: serving chore {Id} ({Name})", id.Value, cached.Chore.Name);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/calendar; charset=utf-8";
        context.Response.Headers["ETag"] = cached.ETag;
        await context.Response.WriteAsync(VTodoMapper.ToIcsString(cached.Chore,
            _calDavSettings.AllDayEvents, _calDavSettings.PreserveScheduledTime));
    }
}
