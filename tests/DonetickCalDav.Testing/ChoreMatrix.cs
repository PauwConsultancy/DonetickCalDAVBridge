using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.Testing;

/// <summary>
/// Named chore scenarios for the ICS snapshot matrix (SN-01): every <see cref="TestChoreFactory"/>
/// scenario plus edge cases of the mapping. Each call returns new instances, so tests may modify them.
/// </summary>
public static class ChoreMatrix
{
    public static IReadOnlyDictionary<string, Func<DonetickChore>> Scenarios { get; } = new Dictionary<string, Func<DonetickChore>>
    {
        ["simple"] = () => TestChoreFactory.Simple(),
        ["due-date"] = () => TestChoreFactory.WithDueDate(),
        ["daily"] = () => TestChoreFactory.DailyRecurring(),
        ["two-labels-in-progress"] = () => TestChoreFactory.WithLabels(),
        ["inactive"] = () => TestChoreFactory.Inactive(),
        ["days-of-week"] = () => TestChoreFactory.WeeklyDaysOfWeek(),
        ["day-of-month"] = () => TestChoreFactory.MonthlyDayOfMonth(),
        ["scheduled-time-hhmm"] = () => WithScheduledTime(8, "14:30", "Europe/Amsterdam"),
        ["scheduled-time-rfc3339"] = () => WithScheduledTime(9, "2025-06-10T06:00:00Z", "Europe/Amsterdam"),
        ["scheduled-time-unknown-zone"] = () => WithScheduledTime(10, "07:15", "Mars/Olympus_Mons"),
        ["special-characters"] = SpecialCharacters,
        ["near-midnight-urgent"] = NearMidnightUrgent,
    };

    /// <summary>The scenario names, for <c>[MemberData]</c>.</summary>
    public static IEnumerable<string> Names => Scenarios.Keys;

    /// <summary>A daily chore whose next due date drifted to 17:45 UTC, with a configured scheduled time.</summary>
    private static DonetickChore WithScheduledTime(int id, string time, string timezone)
    {
        var chore = TestChoreFactory.DailyRecurring(id);
        chore.Name = "Water the plants";
        chore.NextDueDate = new DateTime(2025, 6, 16, 17, 45, 0, DateTimeKind.Utc);
        chore.FrequencyMetadata = new DonetickFrequencyMetadata { Time = time, Timezone = timezone };
        return chore;
    }

    /// <summary>Text that needs escaping or folding in ICS, and a label with an accent.</summary>
    private static DonetickChore SpecialCharacters()
    {
        var chore = TestChoreFactory.WithDueDate(11);
        chore.Name = "Groceries, milk; bread \\ and a name long enough to be folded over more than one content line";
        chore.Description = "Line one\nLine two, with comma; and semicolon";
        chore.LabelsV2 = [new DonetickLabel { Id = 20, Name = "Café & Bar" }];
        chore.Status = ChoreStatus.Paused;
        return chore;
    }

    /// <summary>Due 22:30 UTC (00:30 the next day in Amsterdam, finding F-05), urgent, waiting for approval.</summary>
    private static DonetickChore NearMidnightUrgent()
    {
        var chore = TestChoreFactory.WithDueDate(12, new DateTime(2025, 6, 15, 22, 30, 0, DateTimeKind.Utc));
        chore.Name = "Submit the tax return";
        chore.Priority = ChorePriority.Urgent;
        chore.Status = ChoreStatus.PendingApproval;
        return chore;
    }
}
