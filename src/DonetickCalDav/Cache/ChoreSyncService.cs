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
    private readonly TimeProvider _time;
    private readonly ILogger<ChoreSyncService> _logger;

    /// <summary>Delay before the first sync, so the rest of the app can finish starting.</summary>
    internal static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);

    /// <summary>Number of startup sync attempts before the service falls back to normal polling.</summary>
    internal const int StartupAttempts = 3;

    /// <summary>Back-off after a failed startup attempt: 3 s after the first, 6 s after the second.</summary>
    internal static TimeSpan StartupRetryDelay(int attempt) => TimeSpan.FromSeconds(attempt * 3);

    public ChoreSyncService(
        IDonetickApiClient client,
        ChoreCache cache,
        IOptions<AppSettings> settings,
        TimeProvider time,
        ILogger<ChoreSyncService> logger)
    {
        _client = client;
        _cache = cache;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_settings.Value.Donetick.PollIntervalSeconds);
        _logger.LogInformation("Chore sync service started — polling every {Interval}s", interval.TotalSeconds);

        // Short delay to let the rest of the app finish starting up
        await Task.Delay(StartupDelay, _time, stoppingToken);

        // Initial connectivity check — log clearly if Donetick is unreachable at startup
        await PerformStartupCheckAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncChoresAsync(stoppingToken);
            await Task.Delay(interval, _time, stoppingToken);
        }

        _logger.LogInformation("Chore sync service stopping");
    }

    /// <summary>
    /// Performs the first sync with explicit startup logging.
    /// Retries up to 3 times with backoff so transient failures during container startup
    /// (e.g. Donetick not yet ready) don't leave the cache permanently empty.
    /// </summary>
    private async Task PerformStartupCheckAsync(CancellationToken ct)
    {
        var baseUrl = _settings.Value.Donetick.BaseUrl;

        for (var attempt = 1; attempt <= StartupAttempts; attempt++)
        {
            try
            {
                var chores = await _client.GetAllChoresAsync(ct);
                _cache.UpdateChores(chores);
                _logger.LogInformation(
                    "Startup sync successful — loaded {Count} chores from {BaseUrl}",
                    chores.Count, baseUrl);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Startup sync attempt {Attempt}/{Attempts} failed — Donetick API at {BaseUrl} unreachable",
                    attempt, StartupAttempts, baseUrl);

                if (attempt < StartupAttempts)
                    await Task.Delay(StartupRetryDelay(attempt), _time, ct);
            }
        }

        _logger.LogError(
            "Donetick API at {BaseUrl} unreachable after 3 attempts — " +
            "cache is empty, CalDAV clients will see no tasks. " +
            "Check Donetick__BaseUrl and Donetick__ApiKey configuration.",
            baseUrl);
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
