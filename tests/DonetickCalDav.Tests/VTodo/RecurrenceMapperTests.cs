using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Donetick.Models;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Ical.Net;
using Ical.Net.DataTypes;

namespace DonetickCalDav.Tests.VTodo;

public class RecurrenceMapperTests
{
    // ── Simple frequency types ──────────────────────────────────────────────

    [Theory]
    [InlineData("daily", FrequencyType.Daily)]
    [InlineData("weekly", FrequencyType.Weekly)]
    [InlineData("monthly", FrequencyType.Monthly)]
    [InlineData("yearly", FrequencyType.Yearly)]
    public void ToRRule_SimpleFrequency_MapsCorrectly(string freqType, FrequencyType expectedFreq)
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = freqType;
        chore.Frequency = 2;

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(expectedFreq);
        rule.Interval.Should().Be(2);
    }

    [Fact]
    public void ToRRule_DailyWithZeroFrequency_ClampsIntervalToOne()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "daily";
        chore.Frequency = 0;

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.Interval.Should().Be(1);
    }

    // ── Non-recurring types ─────────────────────────────────────────────────

    [Theory]
    [InlineData("no_repeat")]
    [InlineData("once")]
    [InlineData("trigger")]
    [InlineData("adaptive")]
    [InlineData(null)]
    public void ToRRule_NonRecurring_ReturnsNull(string? freqType)
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = freqType ?? "no_repeat";

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().BeNull();
    }

    // ── Interval frequency type ─────────────────────────────────────────────

    [Theory]
    [InlineData("hours", FrequencyType.Hourly)]
    [InlineData("days", FrequencyType.Daily)]
    [InlineData("weeks", FrequencyType.Weekly)]
    [InlineData("months", FrequencyType.Monthly)]
    public void ToRRule_Interval_MapsUnitCorrectly(string unit, FrequencyType expectedFreq)
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "interval";
        chore.Frequency = 3;
        chore.FrequencyMetadata = new DonetickFrequencyMetadata { Unit = unit };

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(expectedFreq);
        rule.Interval.Should().Be(3);
    }

    [Fact]
    public void ToRRule_Interval_NullUnit_DefaultsToDaily()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "interval";
        chore.Frequency = 1;
        chore.FrequencyMetadata = null;

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(FrequencyType.Daily);
    }

    // ── Days of the week ────────────────────────────────────────────────────

    [Fact]
    public void ToRRule_DaysOfWeek_BuildsWeeklyWithByDay()
    {
        var chore = TestChoreFactory.WeeklyDaysOfWeek();

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(FrequencyType.Weekly);
        rule.ByDay.Should().HaveCount(3);

        var days = rule.ByDay.Select(d => d.DayOfWeek).ToList();
        days.Should().Contain(DayOfWeek.Monday);
        days.Should().Contain(DayOfWeek.Wednesday);
        days.Should().Contain(DayOfWeek.Friday);
    }

    [Fact]
    public void ToRRule_DaysOfWeek_EmptyDays_ReturnsNull()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "days_of_the_week";
        chore.FrequencyMetadata = new DonetickFrequencyMetadata { Days = [] };

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().BeNull();
    }

    [Fact]
    public void ToRRule_DaysOfWeek_NullMetadata_ReturnsNull()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "days_of_the_week";
        chore.FrequencyMetadata = null;

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().BeNull();
    }

    [Fact]
    public void ToRRule_DaysOfWeek_SkipsNullAndInvalidEntries()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "days_of_the_week";
        chore.FrequencyMetadata = new DonetickFrequencyMetadata
        {
            Days = [null, "Monday", "InvalidDay", "Friday"],
        };

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.ByDay.Should().HaveCount(2);
    }

    // ── Day of the month ────────────────────────────────────────────────────

    [Fact]
    public void ToRRule_DayOfMonth_BuildsMonthlyWithByMonthDay()
    {
        var chore = TestChoreFactory.MonthlyDayOfMonth();

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().NotBeNull();
        rule!.Frequency.Should().Be(FrequencyType.Monthly);
        rule.ByMonthDay.Should().BeEquivalentTo([1, 15]);
    }

    [Fact]
    public void ToRRule_DayOfMonth_NonNumericDays_ReturnsNull()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "day_of_the_month";
        chore.FrequencyMetadata = new DonetickFrequencyMetadata
        {
            Days = ["not-a-number"],
        };

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().BeNull();
    }

    [Fact]
    public void ToRRule_DayOfMonth_EmptyDays_ReturnsNull()
    {
        var chore = TestChoreFactory.Simple();
        chore.FrequencyType = "day_of_the_month";
        chore.FrequencyMetadata = new DonetickFrequencyMetadata { Days = [] };

        var rule = RecurrenceMapper.ToRRule(chore);

        rule.Should().BeNull();
    }
}
