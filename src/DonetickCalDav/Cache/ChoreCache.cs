using System.Security.Cryptography;
using System.Text;
using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.Cache;

/// <summary>
/// Immutable snapshot of a cached chore with its computed ETag.
/// </summary>
public sealed record CachedChore(DonetickChore Chore, string ETag);

/// <summary>
/// Thread-safe in-memory cache for Donetick chores.
/// Maintains CTag (collection-level change token) and per-resource ETags
/// to enable efficient CalDAV sync — Apple Calendar only refetches when these change.
/// </summary>
public sealed class ChoreCache
{
    private readonly object _lock = new();
    private readonly ILogger<ChoreCache> _logger;

    private Dictionary<int, CachedChore> _chores = new();
    private Dictionary<string, int> _uidToId = new();
    private string _ctag = GenerateTag();

    public ChoreCache(ILogger<ChoreCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Collection-level change tag. Changes whenever any resource is added, modified, or removed.
    /// Apple Calendar uses this as its primary "has anything changed?" check.
    /// </summary>
    public string CTag
    {
        get { lock (_lock) return _ctag; }
    }

    /// <summary>
    /// Replaces the entire cache with a fresh set of chores from Donetick.
    /// Only bumps the CTag if actual changes are detected.
    /// </summary>
    public void UpdateChores(List<DonetickChore> chores)
    {
        lock (_lock)
        {
            var newDict = new Dictionary<int, CachedChore>(chores.Count);
            var changed = false;

            foreach (var chore in chores)
            {
                var etag = ComputeETag(chore);
                var isUnchanged = _chores.TryGetValue(chore.Id, out var existing) && existing.ETag == etag;

                newDict[chore.Id] = isUnchanged ? existing! : new CachedChore(chore, etag);
                changed = changed || !isUnchanged;
            }

            // Detect deletions: any old IDs not present in the new set
            var hasRemovals = _chores.Keys.Except(newDict.Keys).Any();
            changed = changed || hasRemovals;

            _chores = newDict;

            if (!changed) return;

            _ctag = GenerateTag();
            _logger.LogDebug("Cache updated: {Count} chores, new CTag {CTag}", chores.Count, _ctag);
        }
    }

    /// <summary>Returns a snapshot of all currently cached chores.</summary>
    public List<CachedChore> GetAllChores()
    {
        lock (_lock) return _chores.Values.ToList();
    }

    /// <summary>Returns a single cached chore by Donetick ID, or null if not found.</summary>
    public CachedChore? GetChore(int id)
    {
        lock (_lock) return _chores.GetValueOrDefault(id);
    }

    /// <summary>Removes a chore from the cache and bumps the CTag.</summary>
    public void InvalidateChore(int id)
    {
        lock (_lock)
        {
            if (!_chores.Remove(id)) return;

            _ctag = GenerateTag();
            _logger.LogDebug("Invalidated chore {Id}, new CTag {CTag}", id, _ctag);
        }
    }

    /// <summary>Adds or updates a single chore in the cache and bumps the CTag.</summary>
    public void UpsertChore(DonetickChore chore)
    {
        lock (_lock)
        {
            _chores[chore.Id] = new CachedChore(chore, ComputeETag(chore));
            _ctag = GenerateTag();
            _logger.LogDebug("Upserted chore {Id}, new CTag {CTag}", chore.Id, _ctag);
        }
    }

    /// <summary>
    /// Maps an external UID (from Apple Calendar PUT) to a Donetick chore ID.
    /// Needed because Calendar.app generates its own UUID-based filenames for new tasks.
    /// </summary>
    public void MapUid(string uid, int choreId)
    {
        lock (_lock)
        {
            _uidToId[uid] = choreId;
            _logger.LogDebug("Mapped UID {Uid} to chore {Id}", uid, choreId);
        }
    }

    /// <summary>Resolves an external UID to a Donetick chore ID, or null if unknown.</summary>
    public int? GetIdByUid(string uid)
    {
        lock (_lock) return _uidToId.GetValueOrDefault(uid);
    }

    /// <summary>Generates a fresh opaque tag using a GUID. Wrapped in quotes per HTTP ETag spec.</summary>
    private static string GenerateTag() => $"\"{Guid.NewGuid():N}\"";

    /// <summary>
    /// Computes a deterministic ETag for a chore based on its mutable properties.
    /// Changes to any of these fields will produce a different ETag, triggering a client refetch.
    /// </summary>
    private static string ComputeETag(DonetickChore chore)
    {
        var data = $"{chore.Id}-{chore.UpdatedAt:O}-{chore.Status}-{chore.IsActive}-{chore.Name}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return $"\"{Convert.ToHexString(hash)[..16]}\"";
    }
}
