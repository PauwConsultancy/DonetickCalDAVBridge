using System.Text.Json.Serialization;

namespace DonetickCalDav.Donetick.Models;

/// <summary>Donetick chore status values (from the API "status" integer field).</summary>
public static class ChoreStatus
{
    public const int NoStatus = 0;
    public const int InProgress = 1;
    public const int Paused = 2;
    public const int PendingApproval = 3;
}

/// <summary>Donetick chore priority values (from the API "priority" integer field).</summary>
public static class ChorePriority
{
    public const int None = 0;
    public const int Low = 1;
    public const int Medium = 2;
    public const int High = 3;
    public const int Urgent = 4;
}

/// <summary>
/// Represents a single chore (task) as returned by the Donetick External API.
/// Maps 1:1 to the JSON fields from GET /eapi/v1/chore.
/// </summary>
/// <remarks>
/// FrequencyType values: once, daily, weekly, monthly, yearly, adaptive, interval,
///   days_of_the_week, day_of_the_month, trigger, no_repeat.
/// </remarks>
public sealed class DonetickChore
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("frequencyType")]
    public string FrequencyType { get; set; } = "no_repeat";

    [JsonPropertyName("frequency")]
    public int Frequency { get; set; }

    [JsonPropertyName("frequencyMetadata")]
    public DonetickFrequencyMetadata? FrequencyMetadata { get; set; }

    [JsonPropertyName("nextDueDate")]
    public DateTime? NextDueDate { get; set; }

    [JsonPropertyName("isRolling")]
    public bool IsRolling { get; set; }

    [JsonPropertyName("assignedTo")]
    public int? AssignedTo { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public int CreatedBy { get; set; }

    [JsonPropertyName("circleId")]
    public int CircleId { get; set; }

    [JsonPropertyName("labelsV2")]
    public List<DonetickLabel>? LabelsV2 { get; set; }

    [JsonPropertyName("completionWindow")]
    public int? CompletionWindow { get; set; }

    [JsonPropertyName("deadlineOffset")]
    public int? DeadlineOffset { get; set; }

    [JsonPropertyName("projectId")]
    public int? ProjectId { get; set; }
}

/// <summary>
/// Label attached to a chore, used to generate VTODO CATEGORIES.
/// </summary>
public sealed class DonetickLabel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
