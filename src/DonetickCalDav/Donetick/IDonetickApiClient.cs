using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.Donetick;

/// <summary>
/// Abstraction over the Donetick External API for testability.
/// Implemented by <see cref="DonetickApiClient"/>.
/// </summary>
public interface IDonetickApiClient
{
    Task<List<DonetickChore>> GetAllChoresAsync(CancellationToken ct = default);
    Task<DonetickChore?> CreateChoreAsync(ChoreLiteRequest request, CancellationToken ct = default);
    Task<DonetickChore?> UpdateChoreAsync(int id, ChoreLiteRequest request, CancellationToken ct = default);
    Task DeleteChoreAsync(int id, CancellationToken ct = default);
    Task CompleteChoreAsync(int id, CancellationToken ct = default);
}
