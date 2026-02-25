using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Donetick.Models;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Ical.Net;
using Ical.Net.CalendarComponents;

namespace DonetickCalDav.Tests.VTodo;

public class VTodoMapperTests
{
    // ── ToIcsString ─────────────────────────────────────────────────────────

    [Fact]
    public void ToIcsString_SimpleChore_ProducesValidIcs()
    {
        var chore = TestChoreFactory.Simple();

        var ics = VTodoMapper.ToIcsString(chore);

        ics.Should().Contain("BEGIN:VCALENDAR");
        ics.Should().Contain("BEGIN:VTODO");
        ics.Should().Contain("END:VTODO");
        ics.Should().Contain("END:VCALENDAR");
        ics.Should().Contain("UID:donetick-1@donetick");
        ics.Should().Contain("SUMMARY:Test chore");
        ics.Should().Contain("DESCRIPTION:Test description");
        ics.Should().Contain("STATUS:NEEDS-ACTION");
    }

    [Fact]
    public void ToIcsString_SimpleChore_CanBeParsedBack()
    {
        var chore = TestChoreFactory.Simple();

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);

        calendar.Todos.Should().HaveCount(1);
        var todo = calendar.Todos[0];
        todo.Uid.Should().Be("donetick-1@donetick");
        todo.Summary.Should().Be("Test chore");
        todo.Status.Should().Be("NEEDS-ACTION");
    }

    [Fact]
    public void ToIcsString_ChoreWithDueDate_IncludesDueAndDtStart()
    {
        var chore = TestChoreFactory.WithDueDate();

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Due.Should().NotBeNull();
        todo.DtStart.Should().NotBeNull();
    }

    [Fact]
    public void ToIcsString_ChoreWithoutDueDate_OmitsDue()
    {
        var chore = TestChoreFactory.Simple();
        chore.NextDueDate = null;

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Due.Should().BeNull();
    }

    // ── Labels / Tags ────────────────────────────────────────────────────

    [Fact]
    public void ToIcsString_ChoreWithLabels_IncludesCategories()
    {
        var chore = TestChoreFactory.WithLabels();

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Categories.Should().Contain("Work");
        todo.Categories.Should().Contain("Urgent");
    }

    [Fact]
    public void ToIcsString_ChoreWithLabels_IncludesAppleHashtags()
    {
        var chore = TestChoreFactory.WithLabels();

        var ics = VTodoMapper.ToIcsString(chore);

        // Raw ICS should contain the X-APPLE-HASHTAGS property
        ics.Should().Contain("X-APPLE-HASHTAGS:");
        ics.Should().Contain("Work");
        ics.Should().Contain("Urgent");
    }

    [Fact]
    public void ToIcsString_ChoreWithLabels_AppleHashtagsParsedCorrectly()
    {
        var chore = TestChoreFactory.WithLabels();

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        // Verify the custom property is present and has the right value
        var hashtagProp = todo.Properties.FirstOrDefault(p => p.Name == "X-APPLE-HASHTAGS");
        hashtagProp.Should().NotBeNull();
        var value = hashtagProp!.Value as string;
        value.Should().Contain("Work");
        value.Should().Contain("Urgent");
    }

    [Fact]
    public void ToIcsString_ChoreWithSingleLabel_FormatsCorrectly()
    {
        var chore = TestChoreFactory.Simple();
        chore.LabelsV2 = [new() { Id = 1, Name = "Housework" }];

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Categories.Should().ContainSingle().Which.Should().Be("Housework");

        var hashtagProp = todo.Properties.FirstOrDefault(p => p.Name == "X-APPLE-HASHTAGS");
        hashtagProp.Should().NotBeNull();
        (hashtagProp!.Value as string).Should().Be("Housework");
    }

    [Fact]
    public void ToIcsString_ChoreWithoutLabels_OmitsCategoriesAndHashtags()
    {
        var chore = TestChoreFactory.Simple();
        chore.LabelsV2 = null;

        var ics = VTodoMapper.ToIcsString(chore);

        ics.Should().NotContain("CATEGORIES:");
        ics.Should().NotContain("X-APPLE-HASHTAGS:");
    }

    [Fact]
    public void ToIcsString_ChoreWithEmptyLabels_OmitsCategoriesAndHashtags()
    {
        var chore = TestChoreFactory.Simple();
        chore.LabelsV2 = [];

        var ics = VTodoMapper.ToIcsString(chore);

        ics.Should().NotContain("CATEGORIES:");
        ics.Should().NotContain("X-APPLE-HASHTAGS:");
    }

    [Fact]
    public void ToIcsString_InProgressChore_SetsPercentComplete()
    {
        var chore = TestChoreFactory.Simple();
        chore.Status = 1; // InProgress

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.PercentComplete.Should().Be(50);
    }

    [Fact]
    public void ToIcsString_InactiveChore_StatusIsCancelled()
    {
        var chore = TestChoreFactory.Inactive();

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Status.Should().Be("CANCELLED");
    }

    [Fact]
    public void ToIcsString_RecurringChore_DoesNotIncludeRRule()
    {
        // Donetick manages recurrence server-side; RRULE would cause
        // Calendar.app to generate occurrences client-side, conflicting
        // with the server-managed NextDueDate.
        var chore = TestChoreFactory.DailyRecurring();

        var ics = VTodoMapper.ToIcsString(chore);

        ics.Should().NotContain("RRULE:");
    }

    [Fact]
    public void ToIcsString_HighPriorityChore_SetsPriority()
    {
        var chore = TestChoreFactory.Simple();
        chore.Priority = 3; // High

        var ics = VTodoMapper.ToIcsString(chore);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Priority.Should().Be(3); // iCal high
    }

    [Fact]
    public void ToIcsString_ContainsProductId()
    {
        var chore = TestChoreFactory.Simple();

        var ics = VTodoMapper.ToIcsString(chore);

        // Ical.Net may override the PRODID with its own; just verify one is present
        ics.Should().Contain("PRODID:");
    }

    // ── All-day events ──────────────────────────────────────────────────────

    [Fact]
    public void ToIcsString_AllDayFalse_EmitsDateTimeWithTimezone()
    {
        var chore = TestChoreFactory.WithDueDate();

        var ics = VTodoMapper.ToIcsString(chore, allDayEvents: false);

        // DATE-TIME format includes TZID or UTC suffix (e.g. "20250618T100000Z")
        ics.Should().Contain("DUE");
        // Should NOT contain VALUE=DATE (which signals date-only)
        // The default DATE-TIME serialisation does not emit VALUE=DATE-TIME explicitly
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];
        todo.Due!.HasTime.Should().BeTrue("DUE should include a time component");
    }

    [Fact]
    public void ToIcsString_AllDayTrue_EmitsDateOnly()
    {
        var chore = TestChoreFactory.WithDueDate();

        var ics = VTodoMapper.ToIcsString(chore, allDayEvents: true);

        // VALUE=DATE means no time component
        ics.Should().Contain("VALUE=DATE");

        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];
        todo.Due!.HasTime.Should().BeFalse("DUE should be date-only (all-day)");
        todo.DtStart!.HasTime.Should().BeFalse("DTSTART should be date-only (all-day)");
    }

    [Fact]
    public void ToIcsString_AllDayTrue_PreservesDatePortion()
    {
        // Due date: 2025-06-18 10:00 UTC — all-day should keep the date, drop the time
        var chore = TestChoreFactory.WithDueDate();

        var ics = VTodoMapper.ToIcsString(chore, allDayEvents: true);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Due!.Year.Should().Be(2025);
        todo.Due.Month.Should().Be(6);
        todo.Due.Day.Should().Be(18);
    }

    [Fact]
    public void ToIcsString_AllDayTrue_NoDueDate_OmitsDue()
    {
        var chore = TestChoreFactory.Simple();
        chore.NextDueDate = null;

        var ics = VTodoMapper.ToIcsString(chore, allDayEvents: true);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        todo.Due.Should().BeNull("no due date means DUE is omitted regardless of all-day setting");
    }

    [Fact]
    public void ToIcsString_AllDayTrue_DueAndDtStartAreSameDate()
    {
        var chore = TestChoreFactory.WithDueDate();

        var ics = VTodoMapper.ToIcsString(chore, allDayEvents: true);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        // Both DUE and DTSTART should be the same date-only value
        todo.Due!.Date.Should().Be(todo.DtStart!.Date);
    }

    [Fact]
    public void ToIcsString_DefaultParameter_MatchesFalse()
    {
        var chore = TestChoreFactory.WithDueDate();

        // Default (no argument) should behave like allDayEvents: false
        var icsDefault = VTodoMapper.ToIcsString(chore);
        var icsFalse = VTodoMapper.ToIcsString(chore, allDayEvents: false);

        var calDefault = Calendar.Load(icsDefault);
        var calFalse = Calendar.Load(icsFalse);

        calDefault.Todos[0].Due!.HasTime.Should().Be(calFalse.Todos[0].Due!.HasTime);
    }

    // ── Preserve scheduled time ─────────────────────────────────────────────

    [Fact]
    public void AdjustToScheduledTime_WithTimeAndTimezone_ReplacesTimeInLocalTz()
    {
        // NextDueDate: 2025-06-18 08:00 UTC (= 10:00 in Europe/Amsterdam, CEST = UTC+2)
        // Completed at a different time, so Donetick set 08:00 UTC.
        // Configured scheduled time: 06:00 (in Amsterdam = 04:00 UTC in summer)
        var nextDue = new DateTime(2025, 6, 18, 8, 0, 0, DateTimeKind.Utc);
        var metadata = new DonetickFrequencyMetadata { Time = "06:00", Timezone = "Europe/Amsterdam" };

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, metadata);

        // 06:00 Amsterdam in summer (CEST, UTC+2) = 04:00 UTC
        adjusted.Should().Be(new DateTime(2025, 6, 18, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AdjustToScheduledTime_WithTimeOnly_AdjustsInUtc()
    {
        // No timezone configured — adjust directly in UTC
        var nextDue = new DateTime(2025, 6, 18, 10, 30, 0, DateTimeKind.Utc);
        var metadata = new DonetickFrequencyMetadata { Time = "08:00" };

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, metadata);

        adjusted.Should().Be(new DateTime(2025, 6, 18, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AdjustToScheduledTime_NoMetadata_ReturnsOriginal()
    {
        var nextDue = new DateTime(2025, 6, 18, 10, 0, 0, DateTimeKind.Utc);

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, null);

        adjusted.Should().Be(nextDue);
    }

    [Fact]
    public void AdjustToScheduledTime_EmptyTime_ReturnsOriginal()
    {
        var nextDue = new DateTime(2025, 6, 18, 10, 0, 0, DateTimeKind.Utc);
        var metadata = new DonetickFrequencyMetadata { Time = "", Timezone = "Europe/Amsterdam" };

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, metadata);

        adjusted.Should().Be(nextDue);
    }

    [Fact]
    public void AdjustToScheduledTime_InvalidTime_ReturnsOriginal()
    {
        var nextDue = new DateTime(2025, 6, 18, 10, 0, 0, DateTimeKind.Utc);
        var metadata = new DonetickFrequencyMetadata { Time = "not-a-time", Timezone = "Europe/Amsterdam" };

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, metadata);

        adjusted.Should().Be(nextDue);
    }

    [Fact]
    public void AdjustToScheduledTime_InvalidTimezone_FallsBackToUtc()
    {
        var nextDue = new DateTime(2025, 6, 18, 10, 0, 0, DateTimeKind.Utc);
        var metadata = new DonetickFrequencyMetadata { Time = "08:00", Timezone = "Invalid/Timezone" };

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, metadata);

        // Falls back to UTC adjustment
        adjusted.Should().Be(new DateTime(2025, 6, 18, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AdjustToScheduledTime_WinterTime_HandlesDstCorrectly()
    {
        // January in Amsterdam = CET (UTC+1), not CEST (UTC+2)
        var nextDue = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var metadata = new DonetickFrequencyMetadata { Time = "08:00", Timezone = "Europe/Amsterdam" };

        var adjusted = VTodoMapper.AdjustToScheduledTime(nextDue, metadata);

        // 08:00 Amsterdam in winter (CET, UTC+1) = 07:00 UTC
        adjusted.Should().Be(new DateTime(2025, 1, 15, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ToIcsString_PreserveScheduledTime_AdjustsDueDate()
    {
        var chore = TestChoreFactory.DailyRecurring();
        // Simulate: task scheduled at 08:00 Amsterdam, completed at 10:00 → Donetick set 08:00 UTC
        chore.NextDueDate = new DateTime(2025, 6, 16, 8, 0, 0, DateTimeKind.Utc);
        chore.FrequencyMetadata = new DonetickFrequencyMetadata
        {
            Time = "08:00",
            Timezone = "Europe/Amsterdam",
        };

        var ics = VTodoMapper.ToIcsString(chore, preserveScheduledTime: true);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        // 08:00 Amsterdam CEST = 06:00 UTC
        todo.Due!.Hour.Should().Be(6);
        todo.Due.Minute.Should().Be(0);
    }

    [Fact]
    public void ToIcsString_PreserveScheduledTimeFalse_KeepsOriginalTime()
    {
        var chore = TestChoreFactory.DailyRecurring();
        chore.NextDueDate = new DateTime(2025, 6, 16, 8, 0, 0, DateTimeKind.Utc);
        chore.FrequencyMetadata = new DonetickFrequencyMetadata
        {
            Time = "06:00",
            Timezone = "Europe/Amsterdam",
        };

        var ics = VTodoMapper.ToIcsString(chore, preserveScheduledTime: false);
        var calendar = Calendar.Load(ics);
        var todo = calendar.Todos[0];

        // Should keep original 08:00 UTC, NOT adjust to 04:00 UTC
        todo.Due!.Hour.Should().Be(8);
    }

    [Fact]
    public void ToIcsString_PreserveScheduledTime_NonRecurringNoMetadata_NoChange()
    {
        // Non-recurring task without FrequencyMetadata.Time — should not be affected
        var chore = TestChoreFactory.WithDueDate();
        chore.FrequencyMetadata = null;

        var icsWithout = VTodoMapper.ToIcsString(chore, preserveScheduledTime: false);
        var icsWith = VTodoMapper.ToIcsString(chore, preserveScheduledTime: true);

        var calWithout = Calendar.Load(icsWithout);
        var calWith = Calendar.Load(icsWith);

        calWith.Todos[0].Due!.Value.Should().Be(calWithout.Todos[0].Due!.Value);
    }

    // ── ToCreateRequest ─────────────────────────────────────────────────────

    [Fact]
    public void ToCreateRequest_MapsNameAndDescription()
    {
        var todo = CreateTestTodo("Buy groceries", "Milk and eggs");

        var request = VTodoMapper.ToCreateRequest(todo);

        request.Name.Should().Be("Buy groceries");
        request.Description.Should().Be("Milk and eggs");
    }

    [Fact]
    public void ToCreateRequest_NullSummary_DefaultsToUntitled()
    {
        var todo = CreateTestTodo(null, null);

        var request = VTodoMapper.ToCreateRequest(todo);

        request.Name.Should().Be("Untitled Task");
    }

    [Fact]
    public void ToCreateRequest_EmptyDescription_SetsNull()
    {
        var todo = CreateTestTodo("Task", "");

        var request = VTodoMapper.ToCreateRequest(todo);

        request.Description.Should().BeNull();
    }

    [Fact]
    public void ToCreateRequest_WithDueDate_FormatsAsYyyyMmDd()
    {
        var todo = CreateTestTodo("Task", null);
        todo.Due = new Ical.Net.DataTypes.CalDateTime(new DateTime(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc));

        var request = VTodoMapper.ToCreateRequest(todo);

        request.DueDate.Should().Be("2025-12-25");
    }

    [Fact]
    public void ToCreateRequest_NoDueDate_DueDateIsNull()
    {
        var todo = CreateTestTodo("Task", null);

        var request = VTodoMapper.ToCreateRequest(todo);

        request.DueDate.Should().BeNull();
    }

    // ── ToUpdateRequest ─────────────────────────────────────────────────────

    [Fact]
    public void ToUpdateRequest_MapsNameAndDescription()
    {
        var todo = CreateTestTodo("Updated task", "New description");

        var request = VTodoMapper.ToUpdateRequest(todo);

        request.Name.Should().Be("Updated task");
        request.Description.Should().Be("New description");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Todo CreateTestTodo(string? summary, string? description)
    {
        return new Todo
        {
            Uid = "test-uid@test",
            Summary = summary,
            Description = description,
        };
    }
}
