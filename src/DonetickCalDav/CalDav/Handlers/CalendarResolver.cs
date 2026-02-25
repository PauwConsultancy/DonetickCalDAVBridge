using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DonetickCalDav.Cache;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick.Models;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Snapshot of a virtual CalDAV calendar derived from Donetick labels.
/// </summary>
/// <param name="Slug">URL-safe identifier used in CalDAV paths (e.g. "werk", "tasks").</param>
/// <param name="DisplayName">Human-readable name shown in Apple Reminders / Calendar.</param>
/// <param name="Color">Calendar colour in Apple hex format (RRGGBBAA).</param>
/// <param name="CTag">Collection-level change tag — changes when any resource in this calendar changes.</param>
public sealed record VirtualCalendar(string Slug, string DisplayName, string Color, string CTag);

/// <summary>
/// Resolves virtual CalDAV calendars from Donetick labels.
/// <para>
/// <b>Why separate lists instead of tags?</b><br/>
/// Apple Reminders does not render the standard iCalendar CATEGORIES property
/// (nor the proprietary X-APPLE-HASHTAGS property) for third-party CalDAV accounts —
/// tags only work on native iCloud lists. Splitting chores into separate CalDAV calendar
/// collections per label is the only reliable way to visually group tasks by label in
/// Apple Reminders and Calendar.app.
/// </para>
/// <para>
/// <b>Assignment logic:</b>
/// <list type="bullet">
///   <item>Chore with exactly 1 label → that label's calendar</item>
///   <item>Chore with 0 labels or 2+ labels → default calendar ("tasks")</item>
/// </list>
/// This avoids duplicates: a chore never appears in more than one list.
/// </para>
/// </summary>
public sealed partial class CalendarResolver
{
    /// <summary>URL slug for the default/catch-all calendar. Matches the legacy single-calendar path.</summary>
    public const string DefaultSlug = "tasks";

    private readonly ChoreCache _cache;
    private readonly CalDavSettings _settings;

    public CalendarResolver(ChoreCache cache, IOptions<AppSettings> settings)
    {
        _cache = cache;
        _settings = settings.Value.CalDav;
    }

    /// <summary>Whether multi-list mode is enabled.</summary>
    public bool GroupByLabel => _settings.GroupByLabel;

    /// <summary>
    /// Returns all virtual calendars based on the current cache state.
    /// When <see cref="GroupByLabel"/> is false, returns a single calendar with all chores.
    /// When true, returns the default calendar plus one calendar per unique single-label.
    /// </summary>
    public List<VirtualCalendar> GetCalendars()
    {
        var allChores = _cache.GetAllChores();

        if (!GroupByLabel)
        {
            return
            [
                new VirtualCalendar(
                    DefaultSlug,
                    _settings.CalendarName,
                    _settings.CalendarColor,
                    _cache.CTag)
            ];
        }

        var calendars = new List<VirtualCalendar>();

        // Partition chores by their calendar slug
        var groups = new Dictionary<string, List<CachedChore>>();
        foreach (var cached in allChores)
        {
            var slug = GetSlugForChore(cached.Chore);
            if (!groups.TryGetValue(slug, out var list))
            {
                list = [];
                groups[slug] = list;
            }
            list.Add(cached);
        }

        // Default calendar is always present (even if empty)
        var defaultChores = groups.GetValueOrDefault(DefaultSlug) ?? [];
        calendars.Add(new VirtualCalendar(
            DefaultSlug,
            _settings.DefaultCalendarName,
            _settings.CalendarColor,
            ComputeCTag(defaultChores)));

        // Label calendars — sorted by display name for stable ordering
        var labelSlugs = groups.Keys
            .Where(s => s != DefaultSlug)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var slug in labelSlugs)
        {
            var chores = groups[slug];
            // Derive display name from the first chore's label (they all share the same single label)
            var labelName = chores[0].Chore.LabelsV2![0].Name;
            calendars.Add(new VirtualCalendar(
                slug,
                labelName,
                GenerateColor(labelName),
                ComputeCTag(chores)));
        }

        return calendars;
    }

    /// <summary>
    /// Returns the chores that belong to the given calendar.
    /// </summary>
    public List<CachedChore> GetChoresForCalendar(string slug)
    {
        var allChores = _cache.GetAllChores();

        if (!GroupByLabel)
            return allChores;

        return allChores.Where(c => GetSlugForChore(c.Chore) == slug).ToList();
    }

    /// <summary>
    /// Returns the CTag for a specific calendar.
    /// </summary>
    public string GetCTagForCalendar(string slug)
    {
        if (!GroupByLabel)
            return _cache.CTag;

        var chores = GetChoresForCalendar(slug);
        return ComputeCTag(chores);
    }

    /// <summary>
    /// Checks whether the given slug corresponds to a valid calendar
    /// (either the default or a label-derived calendar with at least one chore).
    /// </summary>
    public bool IsValidCalendar(string slug)
    {
        if (slug == DefaultSlug) return true;
        if (!GroupByLabel) return false;

        // A label calendar is valid if at least one chore belongs to it
        return _cache.GetAllChores().Any(c => GetSlugForChore(c.Chore) == slug);
    }

    /// <summary>
    /// Determines which calendar slug a chore belongs to.
    /// Chores with exactly one label go to that label's calendar;
    /// chores with zero or multiple labels go to the default calendar.
    /// </summary>
    public static string GetSlugForChore(DonetickChore chore)
    {
        if (chore.LabelsV2 is { Count: 1 })
            return ToSlug(chore.LabelsV2[0].Name);

        return DefaultSlug;
    }

    /// <summary>
    /// Converts a label name to a URL-safe slug.
    /// "Privé taken" → "prive-taken", "Work &amp; Life" → "work--life".
    /// </summary>
    public static string ToSlug(string labelName)
    {
        // Normalize to decomposed form so accented chars become base + combining mark
        var normalized = labelName.Normalize(NormalizationForm.FormD);

        // Strip combining marks (accents)
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var slug = sb.ToString()
            .ToLowerInvariant()
            .Replace(' ', '-');

        // Keep only alphanumeric and dashes
        slug = SlugCleanup().Replace(slug, "");

        // Collapse multiple dashes and trim
        slug = MultipleDashes().Replace(slug, "-").Trim('-');

        return string.IsNullOrEmpty(slug) ? "label" : slug;
    }

    /// <summary>
    /// Generates a deterministic calendar colour from a label name.
    /// Uses an HSL colour with fixed saturation and lightness, varying only the hue.
    /// Returns a hex string in Apple's RRGGBBAA format.
    /// </summary>
    public static string GenerateColor(string labelName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(labelName));
        var hue = (BitConverter.ToUInt16(hash, 0) % 360) / 360.0;

        var (r, g, b) = HslToRgb(hue, 0.55, 0.50);
        return $"#{r:X2}{g:X2}{b:X2}FF";
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes a deterministic CTag for a set of chores.
    /// SHA256 of sorted ETags — changes when any chore in the set changes.
    /// Returns a quoted string per HTTP ETag spec.
    /// </summary>
    private static string ComputeCTag(List<CachedChore> chores)
    {
        if (chores.Count == 0)
            return $"\"{Guid.Empty:N}\"";

        var combined = string.Join("|", chores.OrderBy(c => c.Chore.Id).Select(c => c.ETag));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return $"\"{Convert.ToHexString(hash)[..16]}\"";
    }

    /// <summary>Converts HSL (0–1 range) to RGB (0–255).</summary>
    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        double r, g, b;

        if (Math.Abs(s) < 0.001)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    [GeneratedRegex(@"[^a-z0-9-]")]
    private static partial Regex SlugCleanup();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleDashes();
}
