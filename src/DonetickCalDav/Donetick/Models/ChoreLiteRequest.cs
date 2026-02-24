using System.Text.Json.Serialization;

namespace DonetickCalDav.Donetick.Models;

/// <summary>
/// Lightweight request body for creating or updating a chore via the Donetick External API.
/// The eAPI only accepts name, description, and dueDate — priority and recurrence
/// changes are not supported through this endpoint.
/// </summary>
public sealed class ChoreLiteRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>Due date in YYYY-MM-DD format, or null to leave unset.</summary>
    [JsonPropertyName("dueDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DueDate { get; set; }
}
