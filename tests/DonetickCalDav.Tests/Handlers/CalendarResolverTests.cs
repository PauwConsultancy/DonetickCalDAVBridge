using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick.Models;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.Tests.Handlers;

public class CalendarResolverTests
{
    private readonly ChoreCache _cache = new(NullLogger<ChoreCache>.Instance);

    private CalendarResolver CreateResolver(bool groupByLabel = false, string defaultCalendarName = "Algemeen")
    {
        var settings = new AppSettings
        {
            CalDav = new CalDavSettings
            {
                Username = "testuser",
                CalendarName = "Donetick Tasks",
                CalendarColor = "#4A90D9FF",
                GroupByLabel = groupByLabel,
                DefaultCalendarName = defaultCalendarName,
            },
        };
        return new CalendarResolver(_cache, Options.Create(settings));
    }

    // ── ToSlug ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Work", "work")]
    [InlineData("Privé taken", "prive-taken")]
    [InlineData("Work & Life", "work-life")] // & stripped, double dash collapsed to single
    [InlineData("  Spaces  ", "spaces")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("café", "cafe")]
    [InlineData("über cool", "uber-cool")]
    [InlineData("日本語", "label")] // non-latin falls back to "label"
    [InlineData("", "label")]
    public void ToSlug_VariousInputs_ReturnsExpectedSlug(string input, string expected)
    {
        CalendarResolver.ToSlug(input).Should().Be(expected);
    }

    // ── GenerateColor ───────────────────────────────────────────────────────

    [Fact]
    public void GenerateColor_IsDeterministic()
    {
        var color1 = CalendarResolver.GenerateColor("Work");
        var color2 = CalendarResolver.GenerateColor("Work");

        color1.Should().Be(color2);
    }

    [Fact]
    public void GenerateColor_DifferentLabels_ProduceDifferentColors()
    {
        var color1 = CalendarResolver.GenerateColor("Work");
        var color2 = CalendarResolver.GenerateColor("Personal");

        color1.Should().NotBe(color2);
    }

    [Fact]
    public void GenerateColor_ReturnsValidAppleHexFormat()
    {
        var color = CalendarResolver.GenerateColor("Test");

        color.Should().StartWith("#");
        color.Should().HaveLength(9); // #RRGGBBAA
        color.Should().EndWith("FF");
    }

    // ── GetSlugForChore ─────────────────────────────────────────────────────

    [Fact]
    public void GetSlugForChore_SingleLabel_ReturnsLabelSlug()
    {
        var chore = TestChoreFactory.Simple();
        chore.LabelsV2 = [new DonetickLabel { Id = 1, Name = "Work" }];

        CalendarResolver.GetSlugForChore(chore).Should().Be("work");
    }

    [Fact]
    public void GetSlugForChore_NoLabels_ReturnsDefault()
    {
        var chore = TestChoreFactory.Simple();
        chore.LabelsV2 = null;

        CalendarResolver.GetSlugForChore(chore).Should().Be("tasks");
    }

    [Fact]
    public void GetSlugForChore_EmptyLabels_ReturnsDefault()
    {
        var chore = TestChoreFactory.Simple();
        chore.LabelsV2 = [];

        CalendarResolver.GetSlugForChore(chore).Should().Be("tasks");
    }

    [Fact]
    public void GetSlugForChore_MultipleLabels_ReturnsDefault()
    {
        var chore = TestChoreFactory.WithLabels(); // has "Work" and "Urgent"

        CalendarResolver.GetSlugForChore(chore).Should().Be("tasks");
    }

    // ── GroupByLabel = false ─────────────────────────────────────────────────

    [Fact]
    public void GetCalendars_GroupByLabelFalse_ReturnsSingleCalendar()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: false);

        var calendars = resolver.GetCalendars();

        calendars.Should().HaveCount(1);
        calendars[0].Slug.Should().Be("tasks");
        calendars[0].DisplayName.Should().Be("Donetick Tasks");
        calendars[0].Color.Should().Be("#4A90D9FF");
    }

    [Fact]
    public void GetChoresForCalendar_GroupByLabelFalse_ReturnsAllChores()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: false);

        var chores = resolver.GetChoresForCalendar("tasks");

        chores.Should().HaveCount(4); // All chores regardless of labels
    }

    // ── GroupByLabel = true ──────────────────────────────────────────────────

    [Fact]
    public void GetCalendars_GroupByLabelTrue_ReturnsDefaultPlusLabelCalendars()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var calendars = resolver.GetCalendars();

        // Default + "Huishouden" + "Werk" (sorted alphabetically)
        calendars.Should().HaveCount(3);
        calendars[0].Slug.Should().Be("tasks");
        calendars[0].DisplayName.Should().Be("Algemeen");
        calendars[1].Slug.Should().Be("huishouden");
        calendars[1].DisplayName.Should().Be("Huishouden");
        calendars[2].Slug.Should().Be("werk");
        calendars[2].DisplayName.Should().Be("Werk");
    }

    [Fact]
    public void GetCalendars_GroupByLabelTrue_DefaultCalendarUsesConfiguredName()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true, defaultCalendarName: "Overig");

        var calendars = resolver.GetCalendars();

        calendars[0].DisplayName.Should().Be("Overig");
    }

    [Fact]
    public void GetCalendars_GroupByLabelTrue_LabelCalendarsHaveDeterministicColors()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var calendars = resolver.GetCalendars();

        // Default calendar keeps configured color
        calendars[0].Color.Should().Be("#4A90D9FF");

        // Label calendars have generated colors
        calendars[1].Color.Should().StartWith("#");
        calendars[2].Color.Should().StartWith("#");
        calendars[1].Color.Should().NotBe(calendars[2].Color);
    }

    [Fact]
    public void GetChoresForCalendar_DefaultSlug_ReturnsChoresWithZeroOrMultipleLabels()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var chores = resolver.GetChoresForCalendar("tasks");

        // Chore 1 (no labels) + Chore 4 (two labels) = 2
        chores.Should().HaveCount(2);
        chores.Select(c => c.Chore.Id).Should().BeEquivalentTo([1, 4]);
    }

    [Fact]
    public void GetChoresForCalendar_LabelSlug_ReturnsOnlyMatchingChores()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var werkChores = resolver.GetChoresForCalendar("werk");
        werkChores.Should().HaveCount(1);
        werkChores[0].Chore.Id.Should().Be(2);

        var huishoudChores = resolver.GetChoresForCalendar("huishouden");
        huishoudChores.Should().HaveCount(1);
        huishoudChores[0].Chore.Id.Should().Be(3);
    }

    [Fact]
    public void GetChoresForCalendar_UnknownSlug_ReturnsEmpty()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        resolver.GetChoresForCalendar("nonexistent").Should().BeEmpty();
    }

    // ── IsValidCalendar ─────────────────────────────────────────────────────

    [Fact]
    public void IsValidCalendar_DefaultSlug_AlwaysTrue()
    {
        var resolver = CreateResolver(groupByLabel: true);
        resolver.IsValidCalendar("tasks").Should().BeTrue();
    }

    [Fact]
    public void IsValidCalendar_ExistingLabelSlug_True()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        resolver.IsValidCalendar("werk").Should().BeTrue();
    }

    [Fact]
    public void IsValidCalendar_NonExistentSlug_False()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        resolver.IsValidCalendar("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void IsValidCalendar_GroupByLabelFalse_OnlyDefaultIsValid()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: false);

        resolver.IsValidCalendar("tasks").Should().BeTrue();
        resolver.IsValidCalendar("werk").Should().BeFalse();
    }

    // ── CTag per calendar ───────────────────────────────────────────────────

    [Fact]
    public void CTag_DifferentCalendars_HaveDifferentCTags()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var calendars = resolver.GetCalendars();

        var ctags = calendars.Select(c => c.CTag).ToList();
        ctags.Distinct().Should().HaveCount(ctags.Count,
            "each calendar should have a unique CTag");
    }

    [Fact]
    public void CTag_StableWhenOtherCalendarChanges()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var werkCTag1 = resolver.GetCTagForCalendar("werk");

        // Modify a chore in the default calendar (id 1, no labels)
        var modified = TestChoreFactory.Simple(1, "Modified");
        modified.UpdatedAt = DateTime.UtcNow.AddHours(1);
        _cache.UpsertChore(modified);

        var werkCTag2 = resolver.GetCTagForCalendar("werk");

        werkCTag1.Should().Be(werkCTag2,
            "changing a chore in another calendar should not affect this calendar's CTag");
    }

    [Fact]
    public void CTag_ChangesWhenOwnChoreChanges()
    {
        SeedChoresWithLabels();
        var resolver = CreateResolver(groupByLabel: true);

        var werkCTag1 = resolver.GetCTagForCalendar("werk");

        // Modify the chore in "Werk" calendar (id 2)
        var modified = TestChoreFactory.Simple(2, "Modified Werk");
        modified.LabelsV2 = [new DonetickLabel { Id = 1, Name = "Werk" }];
        modified.UpdatedAt = DateTime.UtcNow.AddHours(1);
        _cache.UpsertChore(modified);

        var werkCTag2 = resolver.GetCTagForCalendar("werk");

        werkCTag1.Should().NotBe(werkCTag2,
            "changing a chore in this calendar should update the CTag");
    }

    // ── Empty state ─────────────────────────────────────────────────────────

    [Fact]
    public void GetCalendars_NoChores_GroupByLabelTrue_ReturnsOnlyDefault()
    {
        var resolver = CreateResolver(groupByLabel: true);

        var calendars = resolver.GetCalendars();

        calendars.Should().HaveCount(1);
        calendars[0].Slug.Should().Be("tasks");
        calendars[0].DisplayName.Should().Be("Algemeen");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the cache with four chores covering all label scenarios:
    ///   ID 1: no labels      → default
    ///   ID 2: "Werk"         → werk
    ///   ID 3: "Huishouden"   → huishouden
    ///   ID 4: "Werk"+"Prive" → default (multiple labels)
    /// </summary>
    private void SeedChoresWithLabels()
    {
        var chore1 = TestChoreFactory.Simple(1, "No labels");
        chore1.LabelsV2 = null;

        var chore2 = TestChoreFactory.Simple(2, "Werk task");
        chore2.LabelsV2 = [new DonetickLabel { Id = 1, Name = "Werk" }];

        var chore3 = TestChoreFactory.Simple(3, "Huishouden task");
        chore3.LabelsV2 = [new DonetickLabel { Id = 2, Name = "Huishouden" }];

        var chore4 = TestChoreFactory.Simple(4, "Multi-label task");
        chore4.LabelsV2 = [new DonetickLabel { Id = 1, Name = "Werk" }, new DonetickLabel { Id = 3, Name = "Prive" }];

        _cache.UpdateChores([chore1, chore2, chore3, chore4]);
    }
}
