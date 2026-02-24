using System.Text.Json.Serialization;

namespace DonetickCalDav.Donetick.Models;

/// <summary>
/// Optional metadata for chore frequency configuration.
/// The shape varies by frequencyType — not all fields are populated for every type.
/// </summary>
public sealed class DonetickFrequencyMetadata
{
    /// <summary>Day names or day-of-month numbers depending on frequencyType.</summary>
    [JsonPropertyName("days")]
    public List<string?>? Days { get; set; }

    [JsonPropertyName("months")]
    public List<string?>? Months { get; set; }

    /// <summary>Interval unit for "interval" frequencyType (hours, days, weeks, months).</summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>Time of day in HH:mm format.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("weekNumbers")]
    public List<int>? WeekNumbers { get; set; }

    [JsonPropertyName("occurrences")]
    public List<int?>? Occurrences { get; set; }
}
