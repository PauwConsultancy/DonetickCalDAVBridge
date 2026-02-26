using DonetickCalDav.Cache;
using DonetickCalDav.Donetick;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles DELETE requests to remove a VTODO resource.
/// Forwards the deletion to Donetick and invalidates the cache entry.
/// </summary>
public sealed class DeleteHandler
{
    private readonly ChoreCache _cache;
    private readonly IDonetickApiClient _client;
    private readonly ILogger<DeleteHandler> _logger;

    public DeleteHandler(ChoreCache cache, IDonetickApiClient client, ILogger<DeleteHandler> logger)
    {
        _cache = cache;
        _client = client;
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

        try
        {
            _logger.LogInformation("DELETE: removing chore {Id} from Donetick", id.Value);
            await _client.DeleteChoreAsync(id.Value, context.RequestAborted);
            _cache.InvalidateChore(id.Value);
            context.Response.StatusCode = 204;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DELETE: Donetick API error for chore {Id}", id.Value);
            context.Response.StatusCode = 502;
        }
    }
}
