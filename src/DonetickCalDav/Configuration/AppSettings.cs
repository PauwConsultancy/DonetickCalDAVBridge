using System.ComponentModel.DataAnnotations;

namespace DonetickCalDav.Configuration;

/// <summary>
/// Root configuration binding for all application settings.
/// Populated from appsettings.json and/or environment variables.
/// </summary>
public sealed class AppSettings
{
    public const string SectionName = "AppSettings";

    [Required]
    public DonetickSettings Donetick { get; set; } = new();

    [Required]
    public CalDavSettings CalDav { get; set; } = new();
}

/// <summary>
/// Configuration for the upstream Donetick API connection.
/// </summary>
public sealed class DonetickSettings
{
    /// <summary>Base URL of the Donetick instance (e.g. "http://donetick:8080").</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>External API secret key (sent as "secretkey" header).</summary>
    [Required, MinLength(1)]
    public string ApiKey { get; set; } = "";

    /// <summary>Interval in seconds between chore sync polls. Minimum: 5.</summary>
    [Range(5, 3600)]
    public int PollIntervalSeconds { get; set; } = 30;
}

/// <summary>
/// Configuration for the CalDAV server exposed to Apple Calendar / Reminders.
/// </summary>
public sealed class CalDavSettings
{
    /// <summary>Username for CalDAV Basic Auth. Also used in URL paths.</summary>
    [Required, MinLength(1)]
    public string Username { get; set; } = "user";

    /// <summary>Password for CalDAV Basic Auth.</summary>
    [Required, MinLength(1)]
    public string Password { get; set; } = "pass";

    /// <summary>Display name shown in Apple Reminders for this calendar.</summary>
    public string CalendarName { get; set; } = "Donetick Tasks";

    /// <summary>Calendar colour in Apple hex format (RRGGBBAA).</summary>
    public string CalendarColor { get; set; } = "#4A90D9FF";

    /// <summary>TCP port the CalDAV server listens on.</summary>
    [Range(1, 65535)]
    public int ListenPort { get; set; } = 5232;
}
