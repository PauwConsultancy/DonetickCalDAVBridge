using DonetickCalDav.CalDav.VTodo;
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
