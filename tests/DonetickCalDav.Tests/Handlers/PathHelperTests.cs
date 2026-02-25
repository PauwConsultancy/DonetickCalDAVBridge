using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DonetickCalDav.Tests.Handlers;

public class PathHelperTests
{
    // ── ExtractChoreId ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("/caldav/calendars/user/tasks/donetick-42.ics", 42)]
    [InlineData("/caldav/calendars/user/tasks/donetick-1.ics", 1)]
    [InlineData("/caldav/calendars/user/tasks/donetick-99999.ics", 99999)]
    public void ExtractChoreId_ValidDonetickPath_ReturnsId(string path, int expectedId)
    {
        PathHelper.ExtractChoreId(path).Should().Be(expectedId);
    }

    [Theory]
    [InlineData("/caldav/calendars/user/tasks/")]
    [InlineData("/caldav/calendars/user/tasks/random-file.ics")]
    [InlineData("/caldav/calendars/user/tasks/A1B2C3D4-E5F6-7890-ABCD-EF1234567890.ics")]
    [InlineData("/caldav/")]
    [InlineData("")]
    public void ExtractChoreId_NonDonetickPath_ReturnsNull(string path)
    {
        PathHelper.ExtractChoreId(path).Should().BeNull();
    }

    // ── ExtractUuidFilename ─────────────────────────────────────────────────

    [Theory]
    [InlineData("/caldav/calendars/user/tasks/A1B2C3D4-E5F6-7890-ABCD-EF1234567890.ics", "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    [InlineData("/caldav/calendars/user/tasks/a1b2c3d4-e5f6-7890-abcd-ef1234567890.ics", "a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    public void ExtractUuidFilename_ValidUuidPath_ReturnsUuid(string path, string expectedUuid)
    {
        PathHelper.ExtractUuidFilename(path).Should().Be(expectedUuid);
    }

    [Theory]
    [InlineData("/caldav/calendars/user/tasks/donetick-42.ics")]
    [InlineData("/caldav/calendars/user/tasks/")]
    [InlineData("/caldav/calendars/user/tasks/short-uuid.ics")]
    [InlineData("")]
    public void ExtractUuidFilename_NonUuidPath_ReturnsNull(string path)
    {
        PathHelper.ExtractUuidFilename(path).Should().BeNull();
    }

    // ── ResolveChoreIdFromPath ──────────────────────────────────────────────

    [Fact]
    public void ResolveChoreIdFromPath_DonetickPath_ReturnsIdDirectly()
    {
        var cache = new ChoreCache(NullLogger<ChoreCache>.Instance);

        var id = PathHelper.ResolveChoreIdFromPath(
            "/caldav/calendars/user/tasks/donetick-42.ics", cache);

        id.Should().Be(42);
    }

    [Fact]
    public void ResolveChoreIdFromPath_UuidPath_WithMapping_ReturnsChoreId()
    {
        var cache = new ChoreCache(NullLogger<ChoreCache>.Instance);
        cache.MapUid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890", 42);

        var id = PathHelper.ResolveChoreIdFromPath(
            "/caldav/calendars/user/tasks/A1B2C3D4-E5F6-7890-ABCD-EF1234567890.ics", cache);

        id.Should().Be(42);
    }

    [Fact]
    public void ResolveChoreIdFromPath_UuidPath_WithoutMapping_ReturnsNull()
    {
        var cache = new ChoreCache(NullLogger<ChoreCache>.Instance);

        var id = PathHelper.ResolveChoreIdFromPath(
            "/caldav/calendars/user/tasks/A1B2C3D4-E5F6-7890-ABCD-EF1234567890.ics", cache);

        id.Should().BeNull();
    }

    [Fact]
    public void ResolveChoreIdFromPath_UnrecognizedPath_ReturnsNull()
    {
        var cache = new ChoreCache(NullLogger<ChoreCache>.Instance);

        var id = PathHelper.ResolveChoreIdFromPath("/caldav/calendars/user/tasks/", cache);

        id.Should().BeNull();
    }

    [Fact]
    public void ResolveChoreIdFromPath_DonetickPath_TakesPriorityOverUuid()
    {
        // donetick-{id} pattern should be checked first, even if it looks like a UUID
        var cache = new ChoreCache(NullLogger<ChoreCache>.Instance);

        var id = PathHelper.ResolveChoreIdFromPath(
            "/caldav/calendars/user/tasks/donetick-7.ics", cache);

        id.Should().Be(7);
    }
}
