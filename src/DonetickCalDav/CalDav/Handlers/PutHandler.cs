using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick;
using Ical.Net;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles PUT requests to create or update VTODO resources.
/// Parses the incoming ICS body from Apple Reminders and pushes changes
/// back to Donetick via the External API.
/// </summary>
public sealed class PutHandler
{
    private readonly ChoreCache _cache;
    private readonly DonetickApiClient _client;
    private readonly CalDavSettings _calDavSettings;
    private readonly ILogger<PutHandler> _logger;

    public PutHandler(
        ChoreCache cache,
        DonetickApiClient client,
        IOptions<AppSettings> settings,
        ILogger<PutHandler> logger)
    {
        _cache = cache;
        _client = client;
        _calDavSettings = settings.Value.CalDav;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var icsBody = await new StreamReader(context.Request.Body).ReadToEndAsync();

        // ── Validate the incoming VTODO ──────────────────────────────────────
        var vtodo = ParseVTodo(icsBody);
        if (vtodo == null)
        {
            _logger.LogWarning("PUT: failed to parse VTODO from request body");
            context.Response.StatusCode = 400;
            return;
        }

        // ── Route to update or create ────────────────────────────────────────
        var choreId = ResolveChoreId(path, vtodo.Uid);

        try
        {
            if (choreId.HasValue)
            {
                await HandleUpdate(context, choreId.Value, vtodo);
                return;
            }

            await HandleCreate(context, vtodo);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PUT: Donetick API error for path {Path}", path);
            context.Response.StatusCode = 502;
        }
    }

    /// <summary>Updates an existing chore, or completes it if the VTODO status is COMPLETED.</summary>
    private async Task HandleUpdate(HttpContext context, int choreId, Ical.Net.CalendarComponents.Todo vtodo)
    {
        var (_, shouldComplete) = StatusMapper.FromVTodoStatus(vtodo.Status);

        if (shouldComplete)
        {
            _logger.LogInformation("PUT: completing chore {Id}", choreId);
            await _client.CompleteChoreAsync(choreId);
        }
        else
        {
            var request = VTodoMapper.ToUpdateRequest(vtodo);
            _logger.LogInformation("PUT: updating chore {Id} — {Name}", choreId, request.Name);
            await _client.UpdateChoreAsync(choreId, request);
        }

        // Refresh cache to pick up any server-side changes
        var chores = await _client.GetAllChoresAsync();
        _cache.UpdateChores(chores);

        context.Response.StatusCode = 204;
        SetETagHeader(context, choreId);
    }

    /// <summary>Creates a new chore in Donetick and maps the UID for future lookups.</summary>
    private async Task HandleCreate(HttpContext context, Ical.Net.CalendarComponents.Todo vtodo)
    {
        var request = VTodoMapper.ToCreateRequest(vtodo);
        _logger.LogInformation("PUT: creating new chore — {Name}", request.Name);

        var created = await _client.CreateChoreAsync(request);
        if (created == null)
        {
            _logger.LogError("PUT: Donetick returned null for create request");
            context.Response.StatusCode = 502;
            return;
        }

        _cache.UpsertChore(created);

        // Map the Apple-generated UID to our Donetick ID for future requests
        if (vtodo.Uid != null)
            _cache.MapUid(vtodo.Uid, created.Id);

        context.Response.StatusCode = 201;
        context.Response.Headers["Location"] =
            $"/caldav/calendars/{_calDavSettings.Username}/tasks/donetick-{created.Id}.ics";
        SetETagHeader(context, created.Id);
    }

    /// <summary>Resolves chore ID from URL path, falling back to UID-to-ID cache mapping.</summary>
    private int? ResolveChoreId(string path, string? uid)
    {
        var id = PathHelper.ExtractChoreId(path);
        if (id.HasValue) return id;

        return uid != null ? _cache.GetIdByUid(uid) : null;
    }

    /// <summary>Parses the first VTODO from an ICS body, or returns null on failure.</summary>
    private Ical.Net.CalendarComponents.Todo? ParseVTodo(string icsBody)
    {
        try
        {
            var calendar = Calendar.Load(icsBody);
            return calendar.Todos.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT: ICS body parse failed");
            return null;
        }
    }

    /// <summary>Sets the ETag response header from cache if available.</summary>
    private void SetETagHeader(HttpContext context, int choreId)
    {
        var cached = _cache.GetChore(choreId);
        if (cached != null)
            context.Response.Headers["ETag"] = cached.ETag;
    }
}
