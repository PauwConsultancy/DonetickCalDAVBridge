using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DonetickCalDav.Testing.Snapshots;

/// <summary>
/// Replace values that differ per run with stable tokens before a snapshot comparison. Only volatile
/// values are scrubbed: ETags, label-calendar CTags and DTSTAMP derive from fixed test data and stay.
/// </summary>
public static partial class Scrubbers
{
    /// <summary>The global cache CTag, a random GUID in quotes (also inside sync tokens).</summary>
    public static string CTag(string text) => GlobalCTag().Replace(text, "\"{ctag}\"");

    /// <summary>Any GUID, for example generated identifiers.</summary>
    public static string Guids(string text) => Guid().Replace(text, "{guid}");

    /// <summary><c>uptime_seconds</c> in the health JSON and the uptime on the status page.</summary>
    public static string Uptime(string text) =>
        UptimeHtml().Replace(UptimeJson().Replace(text, "\"uptime_seconds\": 0"), "${prefix}{uptime}${suffix}");

    /// <summary>The application version including the commit hash the SDK appends (<c>0.7.1+abc...</c>).</summary>
    public static string Version(string text) =>
        text.Replace(ApplicationVersion, "{version}")
            .Replace(ApplicationVersion.Replace("+", "\\u002B"), "{version}"); // as System.Text.Json escapes it

    /// <summary>The informational version of the bridge assembly, as the status endpoints report it.</summary>
    public static readonly string ApplicationVersion =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    /// <summary>Random local ports, for example the WireMock stub in the Donetick base URL.</summary>
    public static string LocalPorts(string text) => LocalPort().Replace(text, "http://localhost:{port}");

    /// <summary>The scrubbers every HTTP-level snapshot uses.</summary>
    public static readonly Func<string, string>[] Http = [CTag, Uptime, Version, LocalPorts];

    /// <summary>Re-indents XML consistently (two spaces, LF), keeping element order and content.</summary>
    public static string Xml(string xml)
    {
        var document = XDocument.Parse(xml);
        var declaration = document.Declaration is null ? "" : document.Declaration + "\n";
        return declaration + document.Root!.ToString(SaveOptions.None).Replace("\r\n", "\n");
    }

    [GeneratedRegex("\"[0-9a-f]{32}\"")]
    private static partial Regex GlobalCTag();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex("\"uptime_seconds\":\\s*\\d+")]
    private static partial Regex UptimeJson();

    [GeneratedRegex("(?<prefix>Uptime</span>\\s*<span[^>]*>)[^<]*(?<suffix><)")]
    private static partial Regex UptimeHtml();

    [GeneratedRegex("http://localhost:\\d+")]
    private static partial Regex LocalPort();
}
