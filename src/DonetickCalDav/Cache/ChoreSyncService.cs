using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.Cache;

/// <summary>
/// Background service that periodically polls the Donetick API and refreshes the chore cache.
/// Runs for the lifetime of the application. Errors are logged but do not stop the service.
/// </summary>
public sealed class ChoreSyncService : BackgroundService
{
    private readonly IDonetickApiClient _client;
    private readonly ChoreCache _cache;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<ChoreSyncService> _logger;

    public ChoreSyncService(
        IDonetickApiClient client,
        ChoreCache cache,
        IOptions<AppSettings> settings,
        ILogger<ChoreSyncService> logger)
    {
        _client = client;
        _cache = cache;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_settings.Value.Donetick.PollIntervalSeconds);
        _logger.LogInformation("Chore sync service started — polling every {Interval}s", interval.TotalSeconds);

        // Short delay to let the rest of the app finish starting up
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncChoresAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("Chore sync service stopping");
    }

    /// <summary>
    /// Performs a single sync cycle. Isolated for clarity and testability.
    /// </summary>
    private async Task SyncChoresAsync(CancellationToken ct)
    {
        try
        {
            var chores = await _client.GetAllChoresAsync(ct);
            _cache.UpdateChores(chores);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — not an error
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Donetick API unreachable during sync — will retry next cycle");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during chore sync");
        }
    }
}
