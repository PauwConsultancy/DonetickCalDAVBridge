using DonetickCalDav.CalDav.VTodo;
using FluentAssertions;

namespace DonetickCalDav.Tests.VTodo;

public class StatusMapperTests
{
    // ── ToVTodoStatus ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true, "NEEDS-ACTION")]   // NoStatus
    [InlineData(1, true, "IN-PROCESS")]     // InProgress
    [InlineData(2, true, "NEEDS-ACTION")]   // Paused → NEEDS-ACTION (no iCal equivalent)
    [InlineData(3, true, "IN-PROCESS")]     // PendingApproval → closest match
    public void ToVTodoStatus_ActiveChore_MapsCorrectly(int status, bool isActive, string expected)
    {
        StatusMapper.ToVTodoStatus(status, isActive).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ToVTodoStatus_InactiveChore_AlwaysCancelled(int status)
    {
        StatusMapper.ToVTodoStatus(status, isActive: false).Should().Be("CANCELLED");
    }

    [Fact]
    public void ToVTodoStatus_UnknownStatus_DefaultsToNeedsAction()
    {
        StatusMapper.ToVTodoStatus(99, isActive: true).Should().Be("NEEDS-ACTION");
    }

    // ── FromVTodoStatus ─────────────────────────────────────────────────────

    [Fact]
    public void FromVTodoStatus_Completed_ReturnsNullStatusAndShouldComplete()
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus("COMPLETED");

        status.Should().BeNull();
        shouldComplete.Should().BeTrue();
    }

    [Fact]
    public void FromVTodoStatus_InProcess_ReturnsInProgress()
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus("IN-PROCESS");

        status.Should().Be(1);
        shouldComplete.Should().BeFalse();
    }

    [Fact]
    public void FromVTodoStatus_NeedsAction_ReturnsNoStatus()
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus("NEEDS-ACTION");

        status.Should().Be(0);
        shouldComplete.Should().BeFalse();
    }

    [Fact]
    public void FromVTodoStatus_Cancelled_ReturnsNullStatusNoComplete()
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus("CANCELLED");

        status.Should().BeNull();
        shouldComplete.Should().BeFalse();
    }

    [Fact]
    public void FromVTodoStatus_Null_DefaultsToNoStatus()
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus(null);

        status.Should().Be(0);
        shouldComplete.Should().BeFalse();
    }

    [Fact]
    public void FromVTodoStatus_UnknownValue_DefaultsToNoStatus()
    {
        var (status, shouldComplete) = StatusMapper.FromVTodoStatus("SOMETHING-WEIRD");

        status.Should().Be(0);
        shouldComplete.Should().BeFalse();
    }

    [Fact]
    public void FromVTodoStatus_IsCaseInsensitive()
    {
        var (_, shouldComplete) = StatusMapper.FromVTodoStatus("completed");
        shouldComplete.Should().BeTrue();

        var (status, _) = StatusMapper.FromVTodoStatus("in-process");
        status.Should().Be(1);
    }
}
