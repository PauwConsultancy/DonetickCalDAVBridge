using System.Net.Http.Json;
using System.Text.Json;
using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.Donetick;

/// <summary>
/// HTTP client for the Donetick External API (eAPI v1).
/// Handles all CRUD operations for chores via the /eapi/v1/ endpoints.
/// Authentication is done via the "secretkey" header, configured on the HttpClient at DI registration.
/// </summary>
public sealed class DonetickApiClient : IDonetickApiClient
{
    private const string ChoreEndpoint = "/eapi/v1/chore";

    private readonly HttpClient _http;
    private readonly ILogger<DonetickApiClient> _logger;

    public DonetickApiClient(HttpClient http, ILogger<DonetickApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetches all chores from Donetick. Handles both direct array and wrapper object responses.
    /// </summary>
    public async Task<List<DonetickChore>> GetAllChoresAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching all chores from Donetick");

        var response = await _http.GetAsync(ChoreEndpoint, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var chores = DeserializeChoreResponse(json);

        _logger.LogDebug("Fetched {Count} chores from Donetick", chores.Count);
        return chores;
    }

    /// <summary>
    /// Creates a new chore in Donetick.
    /// </summary>
    public async Task<DonetickChore?> CreateChoreAsync(ChoreLiteRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating chore in Donetick: {Name}", request.Name);

        var response = await _http.PostAsJsonAsync(ChoreEndpoint, request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<DonetickChore>(ct);
        _logger.LogInformation("Created chore {Id} in Donetick: {Name}", created?.Id, request.Name);

        return created;
    }

    /// <summary>
    /// Updates an existing chore in Donetick. Only name, description, and dueDate can be changed via the eAPI.
    /// </summary>
    public async Task<DonetickChore?> UpdateChoreAsync(int id, ChoreLiteRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating chore {Id} in Donetick: {Name}", id, request.Name);

        var response = await _http.PutAsJsonAsync($"{ChoreEndpoint}/{id}", request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DonetickChore>(ct);
    }

    /// <summary>
    /// Permanently deletes a chore from Donetick.
    /// </summary>
    public async Task DeleteChoreAsync(int id, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting chore {Id} from Donetick", id);

        var response = await _http.DeleteAsync($"{ChoreEndpoint}/{id}", ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Deleted chore {Id} from Donetick", id);
    }

    /// <summary>
    /// Marks a chore as completed in Donetick.
    /// </summary>
    public async Task CompleteChoreAsync(int id, CancellationToken ct = default)
    {
        _logger.LogInformation("Completing chore {Id} in Donetick", id);

        var response = await _http.PostAsync($"{ChoreEndpoint}/{id}/complete", null, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Completed chore {Id} in Donetick", id);
    }

    /// <summary>
    /// Resilient deserialization: tries direct array first, then falls back to wrapper object.
    /// The Donetick API may return either format depending on version.
    /// </summary>
    private List<DonetickChore> DeserializeChoreResponse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<DonetickChore>>(json) ?? [];
        }
        catch (JsonException)
        {
            _logger.LogDebug("Direct array deserialization failed, attempting wrapper object format");
        }

        try
        {
            var wrapper = JsonSerializer.Deserialize<ChoreResponseWrapper>(json);
            return wrapper?.Res ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize chore response in any known format");
            return [];
        }
    }

    private sealed class ChoreResponseWrapper
    {
        public List<DonetickChore> Res { get; set; } = [];
    }
}
