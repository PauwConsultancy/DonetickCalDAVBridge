using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.Tests.Helpers;

/// <summary>
/// Convenience factory for creating DonetickChore instances in tests.
/// All methods return a sensible default chore that can be further customized.
/// </summary>
public static class TestChoreFactory
{
    private static readonly DateTime BaseDate = new(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates a simple active chore with no recurrence.</summary>
    public static DonetickChore Simple(int id = 1, string name = "Test chore") => new()
    {
        Id = id,
        Name = name,
        Description = "Test description",
        FrequencyType = "no_repeat",
        Frequency = 0,
        Status = 0,
        Priority = 0,
        IsActive = true,
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };

    /// <summary>Creates a chore with a due date.</summary>
    public static DonetickChore WithDueDate(int id = 2, DateTime? dueDate = null) => new()
    {
        Id = id,
        Name = "Chore with due date",
        FrequencyType = "no_repeat",
        Status = 0,
        Priority = 2,
        IsActive = true,
        NextDueDate = dueDate ?? BaseDate.AddDays(3),
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };

    /// <summary>Creates a daily recurring chore.</summary>
    public static DonetickChore DailyRecurring(int id = 3) => new()
    {
        Id = id,
        Name = "Daily chore",
        FrequencyType = "daily",
        Frequency = 1,
        Status = 0,
        Priority = 0,
        IsActive = true,
        NextDueDate = BaseDate.AddDays(1),
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };

    /// <summary>Creates a chore with labels.</summary>
    public static DonetickChore WithLabels(int id = 4) => new()
    {
        Id = id,
        Name = "Labeled chore",
        FrequencyType = "no_repeat",
        Status = 1, // InProgress
        Priority = 3, // High
        IsActive = true,
        LabelsV2 = [new() { Id = 10, Name = "Work" }, new() { Id = 11, Name = "Urgent" }],
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };

    /// <summary>Creates an inactive (cancelled) chore.</summary>
    public static DonetickChore Inactive(int id = 5) => new()
    {
        Id = id,
        Name = "Inactive chore",
        FrequencyType = "no_repeat",
        Status = 0,
        Priority = 0,
        IsActive = false,
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };

    /// <summary>Creates a weekly days-of-week chore (Mon/Wed/Fri).</summary>
    public static DonetickChore WeeklyDaysOfWeek(int id = 6) => new()
    {
        Id = id,
        Name = "MWF chore",
        FrequencyType = "days_of_the_week",
        Frequency = 1,
        FrequencyMetadata = new DonetickFrequencyMetadata
        {
            Days = ["Monday", "Wednesday", "Friday"],
        },
        Status = 0,
        Priority = 0,
        IsActive = true,
        NextDueDate = BaseDate.AddDays(1),
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };

    /// <summary>Creates a monthly day-of-month chore (1st and 15th).</summary>
    public static DonetickChore MonthlyDayOfMonth(int id = 7) => new()
    {
        Id = id,
        Name = "Monthly chore",
        FrequencyType = "day_of_the_month",
        Frequency = 1,
        FrequencyMetadata = new DonetickFrequencyMetadata
        {
            Days = ["1", "15"],
        },
        Status = 0,
        Priority = 0,
        IsActive = true,
        NextDueDate = BaseDate.AddDays(1),
        CreatedAt = BaseDate,
        UpdatedAt = BaseDate.AddHours(1),
        CreatedBy = 1,
        CircleId = 1,
    };
}
