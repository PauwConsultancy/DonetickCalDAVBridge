using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Donetick.Models;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;

namespace DonetickCalDav.Tests.Properties;

/// <summary>
/// U-02: property-based tests with FsCheck. Instead of a few examples, each property is checked
/// against hundreds of generated inputs; a failure is shrunk to the smallest input that breaks it.
/// </summary>
[Trait("Scenario", "U-02")]
public class PropertyTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // ── CalendarResolver.ToSlug ─────────────────────────────────────────────

    [Property(MaxTest = 500)]
    public bool ToSlug_AnyLabel_IsNonEmptyAndUsesOnlyLowercaseLettersDigitsAndSingleInnerDashes(NonNull<string> label)
    {
        var slug = CalendarResolver.ToSlug(label.Get);

        return slug.Length > 0
               && slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
               && !slug.StartsWith('-') && !slug.EndsWith('-')
               && !slug.Contains("--");
    }

    [Property(MaxTest = 500)]
    public bool ToSlug_AppliedTwice_GivesTheSameSlug(NonNull<string> label)
    {
        var slug = CalendarResolver.ToSlug(label.Get);

        return CalendarResolver.ToSlug(slug) == slug;
    }

    [Property(MaxTest = 200)]
    public bool ToSlug_AsciiWordsJoinedBySpaces_KeepsTheWordsInLowerCase(PositiveInt seed)
    {
        var random = new Random(seed.Get);
        var words = Enumerable.Range(0, random.Next(1, 5))
            .Select(_ => new string(Enumerable.Range(0, random.Next(1, 8)).Select(_ => (char)random.Next('A', 'Z' + 1)).ToArray()))
            .ToArray();

        return CalendarResolver.ToSlug(string.Join(' ', words)) == string.Join('-', words).ToLowerInvariant();
    }

    // ── PathHelper ──────────────────────────────────────────────────────────

    [Property(MaxTest = 300)]
    public bool ExtractChoreId_CanonicalPathOfAnyId_ReturnsThatId(PositiveInt id, bool groupByLabel)
    {
        var calendar = groupByLabel ? "home" : "tasks";

        return PathHelper.ExtractChoreId($"/caldav/calendars/tester/{calendar}/donetick-{id.Get}.ics") == id.Get;
    }

    // ── VTodoMapper.AdjustToScheduledTime ───────────────────────────────────

    /// <summary>Every day of 2025 (both DST changes included) and every quarter of an hour.</summary>
    private static Arbitrary<(DateTime Due, TimeOnly Time)> DueDatesAndTimes() =>
        Gen.Zip(
                Gen.Choose(0, 364).Select(day => new DateTime(2025, 1, 1, 17, 45, 0, DateTimeKind.Utc).AddDays(day)),
                Gen.Choose(0, 95).Select(quarter => new TimeOnly(quarter / 4, quarter % 4 * 15)))
            .ToArbitrary();

    [Property(MaxTest = 1000)]
    public Property AdjustToScheduledTime_ExistingLocalTime_KeepsTheLocalDateAndSetsTheTime()
    {
        return Prop.ForAll(DueDatesAndTimes(), input =>
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(input.Due, Amsterdam));
            var wanted = localDate.ToDateTime(input.Time);
            if (Amsterdam.IsInvalidTime(wanted))
                return true; // covered by the gap test below

            var adjusted = VTodoMapper.AdjustToScheduledTime(input.Due, Metadata(input.Time));
            var local = TimeZoneInfo.ConvertTimeFromUtc(adjusted, Amsterdam);

            return adjusted.Kind == DateTimeKind.Utc
                   && DateOnly.FromDateTime(local) == localDate
                   && TimeOnly.FromDateTime(local) == input.Time;
        });
    }

    [Fact]
    [Trait("KnownIssue", "F-30")]
    public void AdjustToScheduledTime_ScheduledTimeInTheSpringForwardGap_Throws()
    {
        // 02:30 does not exist in Amsterdam on 30 March 2025. ConvertTimeToUtc rejects it, and the
        // exception escapes from ToIcsString, so every PROPFIND or REPORT that includes the chore fails.
        var due = new DateTime(2025, 3, 30, 17, 45, 0, DateTimeKind.Utc);

        Action act = () => VTodoMapper.AdjustToScheduledTime(due, Metadata(new TimeOnly(2, 30)));

        act.Should().Throw<ArgumentException>().WithMessage("*invalid time*");
    }

    private static DonetickFrequencyMetadata Metadata(TimeOnly time) =>
        new() { Time = time.ToString("HH:mm"), Timezone = "Europe/Amsterdam" };
}
