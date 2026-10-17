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
    /// Deserializes the Donetick chore list. Tries the direct array format first, then
    /// the wrapper object format — the API returns one or the other depending on version.
    /// <para>
    /// Throws <see cref="JsonException"/> when neither format matches. Callers (sync service,
    /// PUT handler) treat this as a hard failure: it is truthful about an unknown upstream
    /// shape rather than silently returning an empty list and making clients think the user
    /// has no tasks.
    /// </para>
    /// </summary>
    private List<DonetickChore> DeserializeChoreResponse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<DonetickChore>>(json) ?? [];
        }
        catch (JsonException arrayEx)
        {
            _logger.LogDebug("Direct array deserialization failed, attempting wrapper object format");

            try
            {
                var wrapper = JsonSerializer.Deserialize<ChoreResponseWrapper>(json);
                return wrapper?.Res ?? [];
            }
            catch (JsonException wrapperEx)
            {
                throw new JsonException(
                    "Donetick chore response matched neither the direct array format nor the wrapper object format. " +
                    "The upstream API may have changed its response shape.",
                    new AggregateException(arrayEx, wrapperEx));
            }
        }
    }

    private sealed class ChoreResponseWrapper
    {
        public List<DonetickChore> Res { get; set; } = [];
    }
}
