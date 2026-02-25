using DonetickCalDav.Cache;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DonetickCalDav.Tests.Cache;

public class ChoreCacheTests
{
    private ChoreCache CreateCache() => new(NullLogger<ChoreCache>.Instance);

    // ── UpdateChores ────────────────────────────────────────────────────────

    [Fact]
    public void UpdateChores_PopulatesCache()
    {
        var cache = CreateCache();
        var chores = new[] { TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second") }.ToList();

        cache.UpdateChores(chores);

        cache.GetAllChores().Should().HaveCount(2);
        cache.GetChore(1).Should().NotBeNull();
        cache.GetChore(2).Should().NotBeNull();
    }

    [Fact]
    public void UpdateChores_UpdatesCTag_WhenDataChanges()
    {
        var cache = CreateCache();
        var chore = TestChoreFactory.Simple(1);

        cache.UpdateChores([chore]);
        var ctag1 = cache.CTag;

        chore.Name = "Updated name";
        cache.UpdateChores([chore]);
        var ctag2 = cache.CTag;

        ctag2.Should().NotBe(ctag1);
    }

    [Fact]
    public void UpdateChores_DoesNotUpdateCTag_WhenDataUnchanged()
    {
        var cache = CreateCache();
        var chore = TestChoreFactory.Simple(1);

        cache.UpdateChores([chore]);
        var ctag1 = cache.CTag;

        // Same data, same ETag → CTag should stay the same
        cache.UpdateChores([chore]);
        var ctag2 = cache.CTag;

        ctag2.Should().Be(ctag1);
    }

    [Fact]
    public void UpdateChores_BumpsCTag_WhenChoreRemoved()
    {
        var cache = CreateCache();
        cache.UpdateChores([TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second")]);
        var ctag1 = cache.CTag;

        // Remove chore 2
        cache.UpdateChores([TestChoreFactory.Simple(1)]);
        var ctag2 = cache.CTag;

        ctag2.Should().NotBe(ctag1);
    }

    // ── GetChore ────────────────────────────────────────────────────────────

    [Fact]
    public void GetChore_ExistingId_ReturnsCachedChore()
    {
        var cache = CreateCache();
        cache.UpdateChores([TestChoreFactory.Simple(42, "My chore")]);

        var cached = cache.GetChore(42);

        cached.Should().NotBeNull();
        cached!.Chore.Name.Should().Be("My chore");
        cached.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetChore_MissingId_ReturnsNull()
    {
        var cache = CreateCache();

        cache.GetChore(999).Should().BeNull();
    }

    // ── ETag determinism ────────────────────────────────────────────────────

    [Fact]
    public void ETag_IsDeterministic_ForSameChoreData()
    {
        var cache1 = CreateCache();
        var cache2 = CreateCache();
        var chore = TestChoreFactory.Simple(1);

        cache1.UpdateChores([chore]);
        cache2.UpdateChores([chore]);

        var etag1 = cache1.GetChore(1)!.ETag;
        var etag2 = cache2.GetChore(1)!.ETag;

        etag1.Should().Be(etag2);
    }

    [Fact]
    public void ETag_Changes_WhenChoreMutates()
    {
        var cache = CreateCache();
        var chore = TestChoreFactory.Simple(1);

        cache.UpdateChores([chore]);
        var etag1 = cache.GetChore(1)!.ETag;

        chore.Name = "Changed";
        cache.UpdateChores([chore]);
        var etag2 = cache.GetChore(1)!.ETag;

        etag2.Should().NotBe(etag1);
    }

    [Fact]
    public void ETag_Changes_WhenNextDueDateChanges()
    {
        var cache = CreateCache();
        var chore = TestChoreFactory.WithDueDate();

        cache.UpdateChores([chore]);
        var etag1 = cache.GetChore(chore.Id)!.ETag;

        // Simulate Donetick advancing the due date after task completion
        chore.NextDueDate = chore.NextDueDate!.Value.AddDays(7);
        cache.UpdateChores([chore]);
        var etag2 = cache.GetChore(chore.Id)!.ETag;

        etag2.Should().NotBe(etag1, "ETag must change when NextDueDate changes so Calendar.app refetches");
    }

    [Fact]
    public void CTag_Bumps_WhenNextDueDateChanges()
    {
        var cache = CreateCache();
        var chore = TestChoreFactory.WithDueDate();

        cache.UpdateChores([chore]);
        var ctag1 = cache.CTag;

        chore.NextDueDate = chore.NextDueDate!.Value.AddDays(7);
        cache.UpdateChores([chore]);
        var ctag2 = cache.CTag;

        ctag2.Should().NotBe(ctag1, "CTag must change when any chore's NextDueDate changes");
    }

    // ── UpsertChore ─────────────────────────────────────────────────────────

    [Fact]
    public void UpsertChore_AddsNewChore_BumpsCTag()
    {
        var cache = CreateCache();
        var ctag1 = cache.CTag;

        cache.UpsertChore(TestChoreFactory.Simple(1));
        var ctag2 = cache.CTag;

        ctag2.Should().NotBe(ctag1);
        cache.GetChore(1).Should().NotBeNull();
    }

    [Fact]
    public void UpsertChore_UpdatesExistingChore()
    {
        var cache = CreateCache();
        cache.UpsertChore(TestChoreFactory.Simple(1, "Original"));

        var updated = TestChoreFactory.Simple(1, "Updated");
        cache.UpsertChore(updated);

        cache.GetChore(1)!.Chore.Name.Should().Be("Updated");
    }

    // ── InvalidateChore ─────────────────────────────────────────────────────

    [Fact]
    public void InvalidateChore_RemovesFromCache_BumpsCTag()
    {
        var cache = CreateCache();
        cache.UpdateChores([TestChoreFactory.Simple(1)]);
        var ctag1 = cache.CTag;

        cache.InvalidateChore(1);
        var ctag2 = cache.CTag;

        cache.GetChore(1).Should().BeNull();
        ctag2.Should().NotBe(ctag1);
    }

    [Fact]
    public void InvalidateChore_NonExistentId_DoesNotBumpCTag()
    {
        var cache = CreateCache();
        cache.UpdateChores([TestChoreFactory.Simple(1)]);
        var ctag1 = cache.CTag;

        cache.InvalidateChore(999);
        var ctag2 = cache.CTag;

        ctag2.Should().Be(ctag1);
    }

    // ── UID mapping ─────────────────────────────────────────────────────────

    [Fact]
    public void MapUid_And_GetIdByUid_WorksCorrectly()
    {
        var cache = CreateCache();
        cache.MapUid("B6BB5B67-1234-5678-9ABC-DEF012345678", 42);

        cache.GetIdByUid("B6BB5B67-1234-5678-9ABC-DEF012345678").Should().Be(42);
    }

    [Fact]
    public void GetIdByUid_UnknownUid_ReturnsNull()
    {
        var cache = CreateCache();

        cache.GetIdByUid("unknown-uid").Should().BeNull();
    }

    [Fact]
    public void MapUid_OverwritesPreviousMapping()
    {
        var cache = CreateCache();
        cache.MapUid("test-uid", 10);
        cache.MapUid("test-uid", 20);

        cache.GetIdByUid("test-uid").Should().Be(20);
    }

    // ── GetAllChores ────────────────────────────────────────────────────────

    [Fact]
    public void GetAllChores_EmptyCache_ReturnsEmptyList()
    {
        var cache = CreateCache();

        cache.GetAllChores().Should().BeEmpty();
    }

    [Fact]
    public void GetAllChores_ReturnsSnapshotCopy()
    {
        var cache = CreateCache();
        cache.UpdateChores([TestChoreFactory.Simple(1)]);

        var list1 = cache.GetAllChores();
        var list2 = cache.GetAllChores();

        list1.Should().NotBeSameAs(list2); // Different list instances
        list1.Should().HaveCount(1);
    }

    // ── CTag format ─────────────────────────────────────────────────────────

    [Fact]
    public void CTag_IsQuotedString()
    {
        var cache = CreateCache();

        cache.CTag.Should().StartWith("\"").And.EndWith("\"");
    }
}
