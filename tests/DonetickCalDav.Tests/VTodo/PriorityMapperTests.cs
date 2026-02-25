using DonetickCalDav.CalDav.VTodo;
using FluentAssertions;

namespace DonetickCalDav.Tests.VTodo;

public class PriorityMapperTests
{
    // ── ToVTodoPriority ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]  // None    → undefined
    [InlineData(1, 9)]  // Low     → lowest iCal
    [InlineData(2, 5)]  // Medium  → medium iCal
    [InlineData(3, 3)]  // High    → high iCal
    [InlineData(4, 1)]  // Urgent  → highest iCal
    public void ToVTodoPriority_MapsAllDonetickValues(int donetick, int expectedICal)
    {
        PriorityMapper.ToVTodoPriority(donetick).Should().Be(expectedICal);
    }

    [Fact]
    public void ToVTodoPriority_OutOfRange_DefaultsToUndefined()
    {
        PriorityMapper.ToVTodoPriority(-1).Should().Be(0);
        PriorityMapper.ToVTodoPriority(99).Should().Be(0);
    }

    // ── FromVTodoPriority ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]  // undefined → None
    [InlineData(1, 4)]  // highest   → Urgent
    [InlineData(2, 4)]  // highest   → Urgent
    [InlineData(3, 3)]  // high      → High
    [InlineData(4, 3)]  // high      → High
    [InlineData(5, 2)]  // medium    → Medium
    [InlineData(6, 2)]  // medium    → Medium
    [InlineData(7, 1)]  // lowest    → Low
    [InlineData(8, 1)]  // lowest    → Low
    [InlineData(9, 1)]  // lowest    → Low
    public void FromVTodoPriority_MapsAllICalRanges(int ical, int expectedDonetick)
    {
        PriorityMapper.FromVTodoPriority(ical).Should().Be(expectedDonetick);
    }

    [Fact]
    public void FromVTodoPriority_NegativeValue_DefaultsToNone()
    {
        PriorityMapper.FromVTodoPriority(-1).Should().Be(0);
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RoundTrip_DonetickToICalAndBack_PreservesPriority(int donetick)
    {
        var ical = PriorityMapper.ToVTodoPriority(donetick);
        var roundTripped = PriorityMapper.FromVTodoPriority(ical);

        roundTripped.Should().Be(donetick);
    }
}
