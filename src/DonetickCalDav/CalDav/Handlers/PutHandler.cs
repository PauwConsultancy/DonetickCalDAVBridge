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
    private readonly CalendarResolver _resolver;
    private readonly IDonetickApiClient _client;
    private readonly CalDavSettings _calDavSettings;
    private readonly ILogger<PutHandler> _logger;

    public PutHandler(
        ChoreCache cache,
        CalendarResolver resolver,
        IDonetickApiClient client,
        IOptions<AppSettings> settings,
        ILogger<PutHandler> logger)
    {
        _cache = cache;
        _resolver = resolver;
        _client = client;
        _calDavSettings = settings.Value.CalDav;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Guard against oversized request bodies (e.g. buggy or malicious clients).
        // A single VTODO is typically 0.5–2 KB; 1 MB is generous headroom.
        const int maxBodySize = 1024 * 1024;
        if (context.Request.ContentLength > maxBodySize)
        {
            _logger.LogWarning("PUT {Path} — rejected: Content-Length {Length} exceeds {Max} byte limit",
                path, context.Request.ContentLength, maxBodySize);
            context.Response.StatusCode = 413;
            return;
        }

        using var reader = new StreamReader(context.Request.Body);
        var buffer = new char[maxBodySize];
        var charsRead = await reader.ReadBlockAsync(buffer, 0, maxBodySize);

        // If the buffer is full and there's still more data, the body exceeds the limit.
        if (charsRead == maxBodySize && reader.Peek() != -1)
        {
            _logger.LogWarning("PUT {Path} — rejected: body exceeds {Max} byte limit (Content-Length was absent or wrong)",
                path, maxBodySize);
            context.Response.StatusCode = 413;
            return;
        }

        var icsBody = new string(buffer, 0, charsRead);

        _logger.LogDebug("PUT {Path} — body length: {Length}", path, icsBody.Length);

        // ── Validate the incoming VTODO ──────────────────────────────────────
        var vtodo = ParseVTodo(icsBody);
        if (vtodo == null)
        {
            _logger.LogWarning("PUT {Path} — failed to parse VTODO from body", path);
            context.Response.StatusCode = 400;
            return;
        }

        _logger.LogDebug("PUT {Path} — parsed VTODO: UID={Uid} Summary={Summary} Status={Status}",
            path, vtodo.Uid ?? "(null)", vtodo.Summary ?? "(null)", vtodo.Status ?? "(null)");

        // ── Route to update or create ────────────────────────────────────────
        var choreId = ResolveChoreId(path, vtodo.Uid);
        _logger.LogDebug("PUT {Path} — resolved choreId={ChoreId} (from UID={Uid})",
            path, choreId?.ToString() ?? "null (will create)", vtodo.Uid ?? "(null)");

        try
        {
            if (choreId.HasValue)
            {
                await HandleUpdate(context, choreId.Value, vtodo);
                return;
            }

            // Apple sends a separate COMPLETED VTODO with a new UUID when completing
            // a recurring task. Since we can't map this to an existing chore, we must
            // not create a duplicate. The actual completion is handled via the second PUT
            // that Apple sends to the original donetick-{id}.ics resource.
            var (_, shouldComplete) = StatusMapper.FromVTodoStatus(vtodo.Status);
            if (shouldComplete)
            {
                _logger.LogInformation("PUT: ignoring COMPLETED VTODO with unknown UID {Uid} — " +
                    "likely a recurring task completion handled via the original resource", vtodo.Uid);
                context.Response.StatusCode = 204;
                return;
            }

            await HandleCreate(context, vtodo);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PUT {Path} — Donetick API error: {StatusCode} {Message}",
                path, ex.StatusCode, ex.Message);
            context.Response.StatusCode = 502;
        }
    }

    /// <summary>Updates an existing chore, or completes it if the VTODO status is COMPLETED.</summary>
    private async Task HandleUpdate(HttpContext context, int choreId, Ical.Net.CalendarComponents.Todo vtodo)
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus(vtodo.Status);

        _logger.LogDebug("PUT update: choreId={Id} vtodoStatus={VTodoStatus} mappedStatus={Status} shouldComplete={Complete}",
            choreId, vtodo.Status ?? "(null)", status, shouldComplete);

        if (shouldComplete)
        {
            _logger.LogInformation("PUT update: completing chore {Id} in Donetick", choreId);
            var ct = context.RequestAborted;
            await _client.CompleteChoreAsync(choreId, ct);

            // Completion triggers server-side changes (NextDueDate advancement, status reset)
            // that we cannot predict — full cache refresh is required.
            // Note: concurrent completions may overlap here (both fetch + update the full list),
            // but this is safe — UpdateChores is idempotent and the last write wins with correct data.
            var chores = await _client.GetAllChoresAsync(ct);
            _cache.UpdateChores(chores);
        }
        else
        {
            var request = VTodoMapper.ToUpdateRequest(vtodo);
            _logger.LogDebug("PUT update: updating chore {Id} — name={Name} due={Due}",
                choreId, request.Name, request.DueDate);
            var updated = await _client.UpdateChoreAsync(choreId, request, context.RequestAborted);

            // Update only the affected chore — no need to refetch the entire list.
            if (updated != null)
                _cache.UpsertChore(updated);
        }

        context.Response.StatusCode = 204;

        if (shouldComplete)
        {
            // After completing a recurring chore, Donetick resets status to NEEDS-ACTION
            // and advances NextDueDate. We intentionally omit the ETag here so that the
            // client cannot match its local (COMPLETED) copy against the server's new state.
            // Without an ETag, Calendar.app/Reminders will refetch the resource on the next
            // sync and pick up the server-managed new DUE date and NEEDS-ACTION status.
            _logger.LogDebug("PUT complete: chore {Id} — responded 204 (no ETag, forces client refetch)", choreId);
        }
        else
        {
            SetETagHeader(context, choreId);
            _logger.LogDebug("PUT update: chore {Id} — done, responded 204", choreId);
        }
    }

    /// <summary>Creates a new chore in Donetick and maps the UID for future lookups.</summary>
    private async Task HandleCreate(HttpContext context, Ical.Net.CalendarComponents.Todo vtodo)
    {
        var request = VTodoMapper.ToCreateRequest(vtodo);
        _logger.LogDebug("PUT create: creating new chore — name={Name} due={Due}", request.Name, request.DueDate);

        var created = await _client.CreateChoreAsync(request, context.RequestAborted);
        if (created == null)
        {
            _logger.LogError("PUT create: Donetick returned null for create request");
            context.Response.StatusCode = 502;
            return;
        }

        _cache.UpsertChore(created);

        // Map the Apple-generated UID to our Donetick ID for future requests
        if (vtodo.Uid != null)
        {
            _cache.MapUid(vtodo.Uid, created.Id);
            _logger.LogDebug("PUT create: mapped UID {Uid} → choreId {Id}", vtodo.Uid, created.Id);
        }

        context.Response.StatusCode = 201;
        var slug = _resolver.GroupByLabel
            ? CalendarResolver.GetSlugForChore(created)
            : CalendarResolver.DefaultSlug;
        context.Response.Headers["Location"] =
            $"/caldav/calendars/{_calDavSettings.Username}/{slug}/donetick-{created.Id}.ics";
        SetETagHeader(context, created.Id);
        _logger.LogInformation("PUT create: chore {Id} created — responded 201", created.Id);
    }

    /// <summary>
    /// Resolves chore ID from URL path, VTODO UID, or UID-to-ID cache mapping.
    /// Lookup order:
    ///   1. URL path pattern (donetick-{id}.ics)
    ///   2. VTODO UID pattern (donetick-{id}@donetick) — our own generated UIDs
    ///   3. UID-to-ID cache map — for tasks created via Apple with UUID-based UIDs
    /// </summary>
    private int? ResolveChoreId(string path, string? uid)
    {
        // 1. URL path: /caldav/calendars/user/tasks/donetick-{id}.ics
        var id = PathHelper.ExtractChoreId(path);
        if (id.HasValue)
        {
            _logger.LogDebug("ResolveChoreId: matched URL path pattern → {Id}", id.Value);
            return id;
        }

        // 2. Our generated UID format: donetick-{id}@donetick
        if (uid != null && uid.StartsWith("donetick-") && uid.EndsWith("@donetick"))
        {
            var idPart = uid["donetick-".Length..^"@donetick".Length];
            if (int.TryParse(idPart, out var choreId))
            {
                _logger.LogDebug("ResolveChoreId: matched UID pattern donetick-{Id}@donetick → {Id}", choreId, choreId);
                return choreId;
            }
        }

        // 3. Apple-generated UUID mapped to a Donetick ID (from previous create)
        if (uid != null)
        {
            var mapped = _cache.GetIdByUid(uid);
            if (mapped.HasValue)
                _logger.LogDebug("ResolveChoreId: found UID {Uid} in cache → {Id}", uid, mapped.Value);
            else
                _logger.LogDebug("ResolveChoreId: UID {Uid} not found in cache — treating as new task", uid);
            return mapped;
        }

        _logger.LogDebug("ResolveChoreId: no UID in VTODO, no path match — treating as new task");
        return null;
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
