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

    /// <summary>
    /// When true, creates separate CalDAV calendar lists per Donetick label.
    /// Tasks with exactly one label appear in that label's list; tasks with zero
    /// or multiple labels appear in the default list.
    /// <para>
    /// Background: Apple Reminders does not render CATEGORIES or X-APPLE-HASHTAGS
    /// for third-party CalDAV accounts — tags are only supported on iCloud lists.
    /// Splitting into separate lists is the only way to visually group tasks by
    /// label in Apple Reminders.
    /// </para>
    /// </summary>
    public bool GroupByLabel { get; set; }

    /// <summary>
    /// Display name for the default/catch-all calendar when <see cref="GroupByLabel"/> is enabled.
    /// Tasks with zero or multiple labels appear here.
    /// Ignored when GroupByLabel is false (CalendarName is used instead).
    /// </summary>
    public string DefaultCalendarName { get; set; } = "Algemeen";

    /// <summary>
    /// When true, emits DUE and DTSTART with VALUE=DATE (date only, no time component).
    /// This makes tasks appear as all-day items in Calendar.app instead of at a specific hour.
    /// Reminders.app shows "today" instead of "today at 14:00".
    /// <para>
    /// When false (default), DUE is emitted as DATE-TIME with the full timestamp from Donetick.
    /// </para>
    /// </summary>
    public bool AllDayEvents { get; set; }

    /// <summary>
    /// When true, the bridge replaces the time portion of NextDueDate with the originally
    /// configured scheduled time from <c>FrequencyMetadata.Time</c> (if available).
    /// <para>
    /// Problem: Donetick advances NextDueDate based on completion time. If a task is
    /// scheduled daily at 08:00 but completed at 10:00, the next due date moves to 10:00.
    /// With this option enabled, the bridge corrects it back to 08:00 in the CalDAV output.
    /// </para>
    /// <para>
    /// Only affects recurring tasks that have a configured time in FrequencyMetadata.
    /// Non-recurring tasks and tasks without a configured time are unaffected.
    /// Has no visible effect when <see cref="AllDayEvents"/> is true (no time component shown).
    /// </para>
    /// </summary>
    public bool PreserveScheduledTime { get; set; }

    /// <summary>TCP port the CalDAV server listens on.</summary>
    [Range(1, 65535)]
    public int ListenPort { get; set; } = 5232;

    /// <summary>
    /// Optional TLS settings. When configured, the server listens on HTTPS.
    /// Required for Apple Calendar/Reminders which refuse Basic Auth over plain HTTP.
    /// </summary>
    public TlsSettings? Tls { get; set; }
}

/// <summary>
/// Optional TLS/HTTPS configuration for the CalDAV server.
/// Provide a PFX file or a certificate + key PEM pair.
/// </summary>
public sealed class TlsSettings
{
    /// <summary>Path to a PFX/PKCS12 certificate file.</summary>
    public string? PfxPath { get; set; }

    /// <summary>Password for the PFX file (if encrypted).</summary>
    public string? PfxPassword { get; set; }

    /// <summary>Path to a PEM-encoded certificate file (alternative to PFX).</summary>
    public string? CertPath { get; set; }

    /// <summary>Path to a PEM-encoded private key file (used with CertPath).</summary>
    public string? KeyPath { get; set; }
}
